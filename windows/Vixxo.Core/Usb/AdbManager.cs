using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Vixxo.Core.Usb;

public record AdbDeviceInfo(
    string Serial,
    string State, // "device", "unauthorized", "offline", etc.
    string Model,
    string Product)
{
    public bool IsReady => string.Equals(State, "device", StringComparison.OrdinalIgnoreCase);
}

public sealed class AdbManager : IDisposable
{
    private string? _adbPath;
    private readonly object _lock = new();
    private readonly HashSet<string> _activeReverses = new(); // format: "serial:port"

    public AdbManager(string? customAdbPath = null)
    {
        _adbPath = customAdbPath ?? LocateAdbExecutable();
    }

    public bool IsAdbAvailable => !string.IsNullOrEmpty(_adbPath) && File.Exists(_adbPath);
    public string? AdbPath => _adbPath;

    public static string? LocateAdbExecutable()
    {
        // 1. Check PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "adb.exe");
            if (File.Exists(candidate)) return candidate;
        }

        // 2. Check LOCALAPPDATA Android SDK
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var sdkCandidate = Path.Combine(localAppData, "Android", "Sdk", "platform-tools", "adb.exe");
        if (File.Exists(sdkCandidate)) return sdkCandidate;

        // 3. Check ANDROID_HOME or ANDROID_SDK_ROOT
        var androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME") ??
                          Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        if (!string.IsNullOrEmpty(androidHome))
        {
            var homeCandidate = Path.Combine(androidHome, "platform-tools", "adb.exe");
            if (File.Exists(homeCandidate)) return homeCandidate;
        }

        return null;
    }

    public async Task<List<AdbDeviceInfo>> GetConnectedDevicesAsync(CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return new List<AdbDeviceInfo>();

        var (exitCode, stdout, _) = await RunAdbAsync("devices -l", ct).ConfigureAwait(false);
        if (exitCode != 0) return new List<AdbDeviceInfo>();

        return ParseAdbDevicesOutput(stdout);
    }

    public static List<AdbDeviceInfo> ParseAdbDevicesOutput(string output)
    {
        var devices = new List<AdbDeviceInfo>();
        if (string.IsNullOrWhiteSpace(output)) return devices;

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        // Header line: "List of devices attached"

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
            if (trimmed.StartsWith("* daemon", StringComparison.OrdinalIgnoreCase)) continue;

            // Pattern: <serial>\s+<state>(\s+product:<p>)?(\s+model:<m>)?(\s+device:<d>)?
            var parts = Regex.Split(trimmed, @"\s+");
            if (parts.Length >= 2)
            {
                string serial = parts[0];
                string state = parts[1];

                string model = "Android Device";
                string product = string.Empty;

                for (int i = 2; i < parts.Length; i++)
                {
                    if (parts[i].StartsWith("model:", StringComparison.OrdinalIgnoreCase))
                    {
                        model = parts[i].Substring("model:".Length).Replace('_', ' ');
                    }
                    else if (parts[i].StartsWith("product:", StringComparison.OrdinalIgnoreCase))
                    {
                        product = parts[i].Substring("product:".Length);
                    }
                }

                devices.Add(new AdbDeviceInfo(serial, state, model, product));
            }
        }

        return devices;
    }

    public async Task<bool> SetupReversePortAsync(string serial, int port, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return false;

        string args = $"-s {serial} reverse tcp:{port} tcp:{port}";
        var (exitCode, stdout, stderr) = await RunAdbAsync(args, ct).ConfigureAwait(false);

        if (exitCode == 0)
        {
            lock (_lock)
            {
                _activeReverses.Add($"{serial}:{port}");
            }
            return true;
        }

        System.Diagnostics.Debug.WriteLine($"adb reverse failed ({exitCode}): {stderr} {stdout}");
        return false;
    }

    public async Task<bool> RemoveReversePortAsync(string serial, int port, CancellationToken ct = default)
    {
        if (!IsAdbAvailable) return false;

        string args = $"-s {serial} reverse --remove tcp:{port}";
        var (exitCode, _, _) = await RunAdbAsync(args, ct).ConfigureAwait(false);

        lock (_lock)
        {
            _activeReverses.Remove($"{serial}:{port}");
        }

        return exitCode == 0;
    }

    public async Task RemoveAllActiveReversesAsync()
    {
        if (!IsAdbAvailable) return;

        List<string> entries;
        lock (_lock)
        {
            entries = _activeReverses.ToList();
            _activeReverses.Clear();
        }

        foreach (var entry in entries)
        {
            var parts = entry.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out int port))
            {
                try
                {
                    await RemoveReversePortAsync(parts[0], port).ConfigureAwait(false);
                }
                catch { }
            }
        }
    }

    private async Task<(int exitCode, string stdout, string stderr)> RunAdbAsync(string arguments, CancellationToken ct = default)
    {
        if (_adbPath == null) return (-1, string.Empty, "ADB path is null");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _adbPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);

            return (process.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }

    public void Dispose()
    {
        try
        {
            RemoveAllActiveReversesAsync().GetAwaiter().GetResult();
        }
        catch { }
    }
}
