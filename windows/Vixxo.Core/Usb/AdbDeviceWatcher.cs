namespace Vixxo.Core.Usb;

public sealed class AdbDeviceWatcher : IDisposable
{
    private readonly AdbManager _adbManager;
    private readonly int _port;
    private CancellationTokenSource? _cts;
    private Task? _watchTask;
    private bool _isDisposed;

    private readonly List<AdbDeviceInfo> _lastKnownDevices = new();
    private readonly object _lock = new();

    public event Action<List<AdbDeviceInfo>>? DevicesChanged;
    public event Action<string, bool>? ReverseStatusChanged; // serial, success

    public bool AutoReverseEnabled { get; set; } = true;
    public bool IsWatching => _watchTask != null && !_watchTask.IsCompleted;

    public AdbDeviceWatcher(AdbManager adbManager, int port = 47800)
    {
        _adbManager = adbManager ?? throw new ArgumentNullException(nameof(adbManager));
        _port = port;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_watchTask != null) return;

            _cts = new CancellationTokenSource();
            _watchTask = Task.Run(() => WatchLoopAsync(_cts.Token));
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_lock)
        {
            cts = _cts;
            _cts = null;
            _watchTask = null;
        }

        cts?.Cancel();
        cts?.Dispose();
    }

    private async Task WatchLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var currentDevices = await _adbManager.GetConnectedDevicesAsync(ct).ConfigureAwait(false);

                bool changed = false;
                lock (_lock)
                {
                    if (currentDevices.Count != _lastKnownDevices.Count ||
                        currentDevices.Any(c => !_lastKnownDevices.Any(l => l.Serial == c.Serial && l.State == c.State)))
                    {
                        changed = true;
                        _lastKnownDevices.Clear();
                        _lastKnownDevices.AddRange(currentDevices);
                    }
                }

                if (changed)
                {
                    DevicesChanged?.Invoke(currentDevices);

                    if (AutoReverseEnabled)
                    {
                        foreach (var device in currentDevices.Where(d => d.IsReady))
                        {
                            bool success = await _adbManager.SetupReversePortAsync(device.Serial, _port, ct).ConfigureAwait(false);
                            ReverseStatusChanged?.Invoke(device.Serial, success);
                        }
                    }
                }

                await Task.Delay(2500, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await Task.Delay(5000, ct).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            Stop();
        }
    }
}
