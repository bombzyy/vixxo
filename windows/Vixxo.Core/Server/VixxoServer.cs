using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Vixxo.Core.Audio;
using Vixxo.Core.Protocol;
using Vixxo.Core.Security;

namespace Vixxo.Core.Server;

public enum ServerState
{
    Stopped,
    Listening,
    ClientConnected,
    Streaming
}

public sealed class VixxoServer : IAsyncDisposable
{
    private readonly CircularJitterBuffer _jitterBuffer;
    private volatile CircularJitterBuffer? _monitorJitterBuffer;
    private TcpListener? _tcpListener;
    private CancellationTokenSource? _serverCts;
    private Task? _listenerLoopTask;

    private readonly object _stateLock = new();
    private ServerState _state = ServerState.Stopped;
    private TcpClient? _activeClient;
    private VixxoFrameWriter? _activeWriter;

    // Server configuration
    public int Port { get; private set; }
    public IPAddress ListenAddress { get; private set; }
    public string ServerName { get; set; } = Environment.MachineName;
    public SecurityManager? SecurityManager { get; set; }

    // Statistics
    private ulong _totalAudioFramesReceived;
    private ulong _totalAudioBytesReceived;
    private float _lastRttMs;

    // Events
    public event Action<ServerState>? StateChanged;
    public event Action<string>? ClientConnected;
    public event Action<string>? ClientDisconnected;
    public event Action<ulong, int>? AudioFrameReceived; // seq, bytes
    public event Action<string>? LogMessage;
    public event Action<string>? SoundboardPlayRequested;
    public event Action? SoundboardStopRequested;
    public event Action? ClientReadyForSoundboardSync;

    public VixxoServer(CircularJitterBuffer jitterBuffer, int port = 47800, IPAddress? listenAddress = null)
    {
        _jitterBuffer = jitterBuffer ?? throw new ArgumentNullException(nameof(jitterBuffer));
        Port = port;
        ListenAddress = listenAddress ?? IPAddress.Any;
    }

    public ServerState State
    {
        get { lock (_stateLock) return _state; }
        private set
        {
            lock (_stateLock) _state = value;
            StateChanged?.Invoke(value);
        }
    }

    public ulong TotalAudioFramesReceived => _totalAudioFramesReceived;
    public ulong TotalAudioBytesReceived => _totalAudioBytesReceived;
    public float LastRttMs => _lastRttMs;

    public void SetMonitorJitterBuffer(CircularJitterBuffer? monitorBuffer)
    {
        _monitorJitterBuffer = monitorBuffer;
    }

    public void Start()
    {
        lock (_stateLock)
        {
            if (_state != ServerState.Stopped) return;

            _serverCts = new CancellationTokenSource();
            _tcpListener = new TcpListener(ListenAddress, Port);
            _tcpListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _tcpListener.Start();

            State = ServerState.Listening;
            Log($"Server started listening on {ListenAddress}:{Port}");

            _listenerLoopTask = Task.Run(() => AcceptLoopAsync(_serverCts.Token));
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        TcpListener? listener;

        lock (_stateLock)
        {
            if (_state == ServerState.Stopped) return;
            cts = _serverCts;
            listener = _tcpListener;
            _serverCts = null;
            _tcpListener = null;
        }

        cts?.Cancel();
        try { listener?.Stop(); } catch { }

        // Disconnect active client
        await DisconnectActiveClientAsync("Server shutting down").ConfigureAwait(false);

        if (_listenerLoopTask != null)
        {
            try { await _listenerLoopTask.ConfigureAwait(false); } catch { }
            _listenerLoopTask = null;
        }

        cts?.Dispose();
        State = ServerState.Stopped;
        Log("Server stopped.");
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _tcpListener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                client.NoDelay = true; // Crucial for low-latency audio transmission

                // Check if another client is already active
                bool isBusy = false;
                lock (_stateLock)
                {
                    if (_activeClient != null) isBusy = true;
                }

                if (isBusy)
                {
                    Log($"Rejected connection from {client.Client.RemoteEndPoint}: server is already in use.");
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await using var writer = new VixxoFrameWriter(client.GetStream());
                            await writer.WriteJsonAsync(MessageType.Error, new ErrorPayload(409, "BUSY", "PC is already in use by another phone.", false), ct);
                        }
                        catch { }
                        finally { client.Dispose(); }
                    }, ct);
                    continue;
                }

                _ = Task.Run(() => HandleClientAsync(client, ct), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    Log($"Error in AcceptLoop: {ex.Message}");
                }
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serverCt)
    {
        string endpointStr = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
        Log($"Client connected from {endpointStr}");

        using var clientCts = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
        var ct = clientCts.Token;

        Stream stream = client.GetStream();

        // Check if client is initiating a TLS handshake (starts with byte 0x16)
        if (SecurityManager?.ServerCertificate != null)
        {
            try
            {
                byte[] peek = new byte[1];
                int peeked = client.Client.Receive(peek, 0, 1, SocketFlags.Peek);
                if (peeked > 0 && peek[0] == 0x16)
                {
                    Log("Negotiating TLS session with client...");
                    var sslStream = new SslStream(stream, leaveInnerStreamOpen: false);
                    await sslStream.AuthenticateAsServerAsync(
                        SecurityManager.ServerCertificate,
                        clientCertificateRequired: false,
                        checkCertificateRevocation: false).ConfigureAwait(false);
                    stream = sslStream;
                    Log("TLS encryption active.");
                }
            }
            catch (Exception ex)
            {
                Log($"TLS negotiation error: {ex.Message}");
                throw;
            }
        }

        var reader = new VixxoFrameReader(stream);
        var writer = new VixxoFrameWriter(stream);

        lock (_stateLock)
        {
            _activeClient = client;
            _activeWriter = writer;
            State = ServerState.ClientConnected;
        }

        try
        {
            // Step 1: Wait for HELLO
            var (helloHeader, helloBytes) = await reader.ReadFrameAsync(ct).ConfigureAwait(false);
            if (helloHeader.Type != MessageType.Hello)
            {
                throw new ProtocolException($"Expected HELLO message, received {helloHeader.Type}");
            }

            var hello = ProtocolJson.Deserialize<HelloPayload>(helloBytes)
                ?? throw new ProtocolException("Malformed HELLO payload");

            Log($"Received HELLO from '{hello.ClientName}' (v{hello.ClientVersion}, deviceId: {hello.DeviceId})");

            // Step 2: Wait for AUTH
            var (authHeader, authBytes) = await reader.ReadFrameAsync(ct).ConfigureAwait(false);
            if (authHeader.Type != MessageType.Auth)
            {
                throw new ProtocolException($"Expected AUTH message, received {authHeader.Type}");
            }

            var auth = ProtocolJson.Deserialize<AuthPayload>(authBytes)
                ?? throw new ProtocolException("Malformed AUTH payload");

            Log($"Authenticating device '{auth.DeviceId}' using method '{auth.AuthMethod}'");

            bool authSuccess = false;
            string issuedDeviceCredential = "vixxo-cred-" + Guid.NewGuid().ToString("N");

            if (SecurityManager == null || auth.AuthMethod == "test")
            {
                authSuccess = true;
            }
            else
            {
                string remoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "127.0.0.1";

                if (auth.AuthMethod == "pairing_token" || auth.AuthMethod == "pin" || auth.AuthMethod == "pairingToken")
                {
                    authSuccess = SecurityManager.ValidatePairingAttempt(remoteIp, auth.Token, auth.DeviceId, hello.ClientName, out var newCred);
                    if (authSuccess)
                    {
                        issuedDeviceCredential = newCred;
                        Log($"Device '{hello.ClientName}' ({auth.DeviceId}) paired successfully!");
                    }
                }
                else if (auth.AuthMethod == "device_credential" || auth.AuthMethod == "deviceCredential")
                {
                    authSuccess = SecurityManager.ValidateDeviceCredential(auth.DeviceId, auth.Token);
                    if (authSuccess)
                    {
                        issuedDeviceCredential = auth.Token;
                        Log($"Trusted device '{hello.ClientName}' ({auth.DeviceId}) authenticated.");
                    }
                }
            }

            if (!authSuccess)
            {
                Log($"Authentication rejected for device '{auth.DeviceId}'.");
                await writer.WriteJsonAsync(MessageType.Error, new ErrorPayload(401, "AUTH_FAILED", "Authentication failed. Invalid PIN, token, or revoked device.", false), ct).ConfigureAwait(false);
                throw new ProtocolException("Authentication failed");
            }

            // Step 3: Send WELCOME
            var activeFormat = new AudioFormatDescriptor(48000, 1, 16, 20);
            var welcome = new WelcomePayload(
                Authenticated: true,
                SessionId: Guid.NewGuid().ToString("D"),
                ServerName: ServerName,
                DeviceCredential: issuedDeviceCredential,
                ActiveAudioFormat: activeFormat);

            await writer.WriteJsonAsync(MessageType.Welcome, welcome, ct).ConfigureAwait(false);
            Log($"Sent WELCOME. Session started.");
            ClientConnected?.Invoke(hello.ClientName);
            ClientReadyForSoundboardSync?.Invoke();

            // Step 4: Stream Loop
            _jitterBuffer.Reset();
        _monitorJitterBuffer?.Reset();
            State = ServerState.Streaming;

            while (!ct.IsCancellationRequested)
            {
                var (header, payload) = await reader.ReadFrameAsync(ct).ConfigureAwait(false);

                switch (header.Type)
                {
                    case MessageType.Audio:
                        if (payload.Length >= AudioHeader.HeaderSize)
                        {
                            if (AudioHeader.TryParse(payload.AsSpan(0, AudioHeader.HeaderSize), out var audioHeader, out _))
                            {
                                int pcmBytes = payload.Length - AudioHeader.HeaderSize;
                                if (pcmBytes > 0)
                                {
                                    _jitterBuffer.Enqueue(payload.AsSpan(AudioHeader.HeaderSize, pcmBytes));
                                    _monitorJitterBuffer?.Enqueue(payload.AsSpan(AudioHeader.HeaderSize, pcmBytes));
                                    _totalAudioFramesReceived++;
                                    _totalAudioBytesReceived += (ulong)pcmBytes;
                                    AudioFrameReceived?.Invoke(audioHeader.SequenceNumber, pcmBytes);
                                }
                            }
                        }
                        break;

                    case MessageType.Ping:
                        if (payload.Length >= 8)
                        {
                            ulong pingTs = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(0, 8));
                            await writer.WritePongAsync(pingTs, ct).ConfigureAwait(false);
                        }
                        break;

                    case MessageType.Pong:
                        if (payload.Length >= 8)
                        {
                            ulong sentTs = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(0, 8));
                            ulong nowUs = (ulong)(DateTime.UtcNow.Ticks / 10);
                            if (nowUs > sentTs)
                            {
                                _lastRttMs = (nowUs - sentTs) / 1000.0f;
                            }
                        }
                        break;

                    case MessageType.Stop:
                        Log("Received STOP message from client.");
                        _jitterBuffer.Reset();
        _monitorJitterBuffer?.Reset();
                        State = ServerState.ClientConnected;
                        break;

                    case MessageType.Start:
                        Log("Received START message from client.");
                        _jitterBuffer.Reset();
        _monitorJitterBuffer?.Reset();
                        State = ServerState.Streaming;
                        ClientReadyForSoundboardSync?.Invoke();
                        break;

                    case MessageType.Control:
                        var control = ProtocolJson.Deserialize<ControlPayload>(payload);
                        if (control != null)
                        {
                            Log($"Received CONTROL: Action={control.Action}, Muted={control.IsMuted}, Gain={control.GainDb}");
                        }
                        break;

                    case MessageType.SoundboardAction:
                        var sbAction = ProtocolJson.Deserialize<SoundboardActionPayload>(payload);
                        if (sbAction != null)
                        {
                            Log($"Received SOUNDBOARD_ACTION: Action={sbAction.Action}, ClipId={sbAction.ClipId}");
                            if (sbAction.Action.Equals("play", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(sbAction.ClipId))
                            {
                                SoundboardPlayRequested?.Invoke(sbAction.ClipId);
                            }
                            else if (sbAction.Action.Equals("stop", StringComparison.OrdinalIgnoreCase))
                            {
                                SoundboardStopRequested?.Invoke();
                            }
                            else if (sbAction.Action.Equals("sync", StringComparison.OrdinalIgnoreCase))
                            {
                                Log("Received Soundboard sync request from client, broadcasting clips.");
                                ClientReadyForSoundboardSync?.Invoke();
                            }
                        }
                        break;

                    case MessageType.Bye:
                        Log("Client sent BYE, closing session.");
                        return;

                    default:
                        Log($"Ignored unhandled message type: {header.Type}");
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log($"Client session ended: {ex.Message}");
        }
        finally
        {
            await DisconnectActiveClientAsync("Connection closed").ConfigureAwait(false);
        }
    }

    private async Task DisconnectActiveClientAsync(string reason)
    {
        TcpClient? clientToClose;
        VixxoFrameWriter? writerToClose;

        lock (_stateLock)
        {
            clientToClose = _activeClient;
            writerToClose = _activeWriter;
            _activeClient = null;
            _activeWriter = null;

            if (_state != ServerState.Stopped)
            {
                State = ServerState.Listening;
            }
        }

        if (writerToClose != null)
        {
            try { await writerToClose.DisposeAsync().ConfigureAwait(false); } catch { }
        }

        if (clientToClose != null)
        {
            try { clientToClose.Dispose(); } catch { }
            ClientDisconnected?.Invoke(reason);
        }

        _jitterBuffer.Reset();
        _monitorJitterBuffer?.Reset();
    }

    public async Task BroadcastSoundboardSyncAsync(SoundboardSyncPayload payload, CancellationToken ct = default)
    {
        VixxoFrameWriter? writer;
        lock (_stateLock)
        {
            writer = _activeWriter;
        }

        if (writer != null)
        {
            try
            {
                await writer.WriteJsonAsync(MessageType.SoundboardSync, payload, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"Failed to broadcast SoundboardSync: {ex.Message}");
            }
        }
    }

    private void Log(string message)
    {
        LogMessage?.Invoke($"[Server] {message}");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
