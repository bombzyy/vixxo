using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vixxo.Core.Audio.Ipc;

public class IpcResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("sample_rate")]
    public int SampleRate { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

public class IpcStatsResponse : IpcResponse
{
    [JsonPropertyName("f0_ms")]
    public float F0Ms { get; set; }

    [JsonPropertyName("feature_ms")]
    public float FeatureMs { get; set; }

    [JsonPropertyName("generator_ms")]
    public float GeneratorMs { get; set; }

    [JsonPropertyName("total_ms")]
    public float TotalMs { get; set; }

    [JsonPropertyName("chunks_processed")]
    public long ChunksProcessed { get; set; }

    [JsonPropertyName("peak_vram_mb")]
    public float PeakVramMb { get; set; }

    [JsonPropertyName("device")]
    public string? Device { get; set; }
}

/// <summary>
/// Asynchronous Named Pipe control client that manages the background Python RVC worker process,
/// sends commands (load_model, set_params, stats), monitors heartbeats, and handles crashes.
/// </summary>
public sealed class NamedPipeVoiceClient : IAsyncDisposable
{
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipeStream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Process? _workerProcess;

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly System.Timers.Timer _heartbeatTimer;

    private long _messageIdCounter;
    private bool _isDisposed;

    public bool IsConnected => _pipeStream?.IsConnected == true;
    public bool IsWorkerProcessRunning => _workerProcess != null && !_workerProcess.HasExited;

    public event Action<string>? WorkerOutputReceived;
    public event Action<string>? WorkerErrorReceived;
    public event Action? WorkerExited;
    public event Action? WorkerDisconnected;

    public NamedPipeVoiceClient(string prefix = "vixxo_voice")
    {
        _pipeName = $"{prefix}_control";

        _heartbeatTimer = new System.Timers.Timer(1000)
        {
            AutoReset = true,
            Enabled = false
        };
        _heartbeatTimer.Elapsed += async (_, _) => await OnHeartbeatTickAsync();
    }

    /// <summary>
    /// Spawns the Python RVC worker process and establishes Named Pipe control connection.
    /// </summary>
    public async Task<bool> StartAsync(
        string pythonExePath,
        string scriptPath,
        string arguments = "",
        int connectTimeoutMs = 15000,
        CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            return true;
        }

        // Check if an existing worker is already running on the pipe
        if (await ConnectAsync(timeoutMs: 500, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            _heartbeatTimer.Start();
            return true;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = pythonExePath,
                Arguments = $"-u \"{scriptPath}\" {arguments}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory
            };

            Debug.WriteLine($"[NamedPipeVoiceClient] Launching worker: {startInfo.FileName} {startInfo.Arguments}");
            _workerProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            _workerProcess.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Debug.WriteLine($"[Worker StdOut] {e.Data}");
                    WorkerOutputReceived?.Invoke(e.Data);
                }
            };

            _workerProcess.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Debug.WriteLine($"[Worker StdErr] {e.Data}");
                    WorkerErrorReceived?.Invoke(e.Data);
                }
            };

            _workerProcess.Exited += (_, _) =>
            {
                Debug.WriteLine("[NamedPipeVoiceClient] Worker process terminated unexpectedly!");
                WorkerExited?.Invoke();
                WorkerDisconnected?.Invoke();
            };

            if (!_workerProcess.Start())
            {
                Debug.WriteLine("[NamedPipeVoiceClient] Failed to start worker process.");
                return false;
            }

            _workerProcess.BeginOutputReadLine();
            _workerProcess.BeginErrorReadLine();

            // Connect to Named Pipe
            bool connected = await ConnectAsync(connectTimeoutMs, cancellationToken);
            if (connected)
            {
                _heartbeatTimer.Start();
            }
            return connected;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NamedPipeVoiceClient] StartAsync error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ConnectAsync(int timeoutMs = 15000, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                _pipeStream?.Dispose();
                _pipeStream = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

                await _pipeStream.ConnectAsync(1000, cancellationToken);

                var utf8NoBom = new UTF8Encoding(false);
                _reader = new StreamReader(_pipeStream, utf8NoBom, leaveOpen: true);
                _writer = new StreamWriter(_pipeStream, utf8NoBom, leaveOpen: true) { AutoFlush = true };

                return true;
            }
            catch (TimeoutException)
            {
                // Pipe not ready yet, continue polling
            }
            catch (IOException)
            {
                // Pipe not created yet, continue polling
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NamedPipeVoiceClient] Connect error: {ex.Message}");
            }

            try
            {
                await Task.Delay(250, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        return false;
    }

    public async Task<TResponse?> SendCommandAsync<TResponse>(object command, int timeoutMs = 5000) where TResponse : IpcResponse
    {
        if (!IsConnected || _writer == null || _reader == null)
        {
            return null;
        }

        await _sendLock.WaitAsync();
        try
        {
            string json = JsonSerializer.Serialize(command);
            await _writer.WriteLineAsync(json.AsMemory());
            await _writer.FlushAsync();

            using var cts = new CancellationTokenSource(timeoutMs);
            string? line = await _reader.ReadLineAsync(cts.Token);
            Console.WriteLine($"[NamedPipeVoiceClient] Received line: '{line}'");
            if (string.IsNullOrEmpty(line))
            {
                return null;
            }

            return JsonSerializer.Deserialize<TResponse>(line);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NamedPipeVoiceClient] SendCommand error: {ex.GetType().Name} - {ex.Message}");
            return null;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<bool> PingAsync()
    {
        long id = Interlocked.Increment(ref _messageIdCounter);
        var resp = await SendCommandAsync<IpcResponse>(new { cmd = "ping", id });
        return resp?.Status == "pong";
    }

    public async Task<bool> LoadModelAsync(string modelPath, string? indexPath = null)
    {
        long id = Interlocked.Increment(ref _messageIdCounter);
        var resp = await SendCommandAsync<IpcResponse>(new
        {
            cmd = "load_model",
            model_path = modelPath,
            index_path = indexPath,
            id
        }, timeoutMs: 15000);

        return resp?.Status == "ok";
    }

    public async Task<bool> SetParametersAsync(int pitch, float indexRate, int sid = 0)
    {
        long id = Interlocked.Increment(ref _messageIdCounter);
        var resp = await SendCommandAsync<IpcResponse>(new
        {
            cmd = "set_params",
            pitch,
            index_rate = indexRate,
            sid,
            id
        });

        return resp?.Status == "ok";
    }

    public async Task<IpcStatsResponse?> GetStatsAsync()
    {
        long id = Interlocked.Increment(ref _messageIdCounter);
        return await SendCommandAsync<IpcStatsResponse>(new { cmd = "stats", id });
    }

    public async Task ShutdownAsync()
    {
        _heartbeatTimer.Stop();

        try
        {
            if (IsConnected)
            {
                long id = Interlocked.Increment(ref _messageIdCounter);
                await SendCommandAsync<IpcResponse>(new { cmd = "shutdown", id }, timeoutMs: 2000);
            }
        }
        catch { }

        CleanupProcess();
    }

    private async Task OnHeartbeatTickAsync()
    {
        if (!IsConnected)
        {
            return;
        }

        bool ok = await PingAsync();
        if (!ok)
        {
            Debug.WriteLine("[NamedPipeVoiceClient] Heartbeat missed!");
            WorkerDisconnected?.Invoke();
        }
    }

    private void CleanupProcess()
    {
        if (_workerProcess != null && !_workerProcess.HasExited)
        {
            try
            {
                if (!_workerProcess.WaitForExit(1000))
                {
                    _workerProcess.Kill(entireProcessTree: true);
                }
            }
            catch { }
            _workerProcess.Dispose();
            _workerProcess = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try { await ShutdownAsync().ConfigureAwait(false); } catch { }
        try { _heartbeatTimer?.Dispose(); } catch { }
        try { _writer?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _pipeStream?.Dispose(); } catch { }
        try { _sendLock?.Dispose(); } catch { }
    }
}
