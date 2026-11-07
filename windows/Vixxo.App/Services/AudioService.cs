using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NAudio.CoreAudioApi;
using Vixxo.Core.Audio;
using Vixxo.Core.Discovery;
using Vixxo.Core.Security;
using Vixxo.Core.Server;
using Vixxo.Core.Usb;

namespace Vixxo.App.Services;

public sealed class AudioService : IAsyncDisposable
{
    private readonly CircularJitterBuffer _mainJitterBuffer;
    private readonly WasapiAudioRenderer _mainRenderer;

    private CircularJitterBuffer? _monitorJitterBuffer;
    private WasapiAudioRenderer? _monitorRenderer;

    private VixxoServer? _server;
    private UdpDiscoveryResponder? _discoveryResponder;
    private AdbDeviceWatcher? _adbWatcher;
    private bool _isDisposed;

    public SecurityManager SecurityManager { get; }
    public AdbManager AdbManager { get; }
    public AdbDeviceWatcher? AdbWatcher => _adbWatcher;
    public SoundboardMixer SoundboardMixer { get; }

    public AudioService()
    {
        _mainJitterBuffer = new CircularJitterBuffer(48000, 1, 16, capacityMs: 500, LatencyMode.Balanced);
        SoundboardMixer = new SoundboardMixer();
        _mainRenderer = new WasapiAudioRenderer(_mainJitterBuffer)
        {
            SoundboardMixer = SoundboardMixer
        };
        _mainRenderer.SetDriftCompensator(new DriftCompensator(_mainJitterBuffer, 80));
        SecurityManager = new SecurityManager();
        AdbManager = new AdbManager();
    }

    public WasapiAudioRenderer MainRenderer => _mainRenderer;
    public WasapiAudioRenderer? MonitorRenderer => _monitorRenderer;
    public VixxoServer? Server => _server;

    public bool IsMonitoringActive => _monitorRenderer?.IsActive ?? false;

    public void StartServer(int port = 47800)
    {
        StopServer();

        _server = new VixxoServer(_mainJitterBuffer, port, IPAddress.Any)
        {
            SecurityManager = SecurityManager
        };

        if (_monitorJitterBuffer != null)
        {
            _server.SetMonitorJitterBuffer(_monitorJitterBuffer);
        }

        _server.Start();

        try
        {
            _discoveryResponder = new UdpDiscoveryResponder(UdpDiscoveryResponder.DefaultDiscoveryPort)
            {
                ServerName = Environment.MachineName,
                TcpPort = port,
                CertificateFingerprint = SecurityManager.ServerCertificateFingerprint,
                LocalAddresses = GetLocalIPv4Addresses()
            };
            _discoveryResponder.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start UDP discovery: {ex.Message}");
        }

        try
        {
            if (AdbManager.IsAdbAvailable)
            {
                _adbWatcher = new AdbDeviceWatcher(AdbManager, port);
                _adbWatcher.Start();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start ADB watcher: {ex.Message}");
        }
    }

    public void StopServer()
    {
        if (_adbWatcher != null)
        {
            try
            {
                _adbWatcher.Stop();
                _adbWatcher.Dispose();
            }
            catch { }
            _adbWatcher = null;
        }

        if (_discoveryResponder != null)
        {
            try
            {
                _discoveryResponder.StopAsync().GetAwaiter().GetResult();
            }
            catch { }
            _discoveryResponder = null;
        }

        if (_server != null)
        {
            try
            {
                _server.StopAsync().GetAwaiter().GetResult();
            }
            catch { }
            _server = null;
        }
    }

    public bool StartMainRenderer(string deviceId, int latencyMs = 50)
    {
        try
        {
            var device = AudioDeviceManager.GetDeviceById(deviceId);
            if (device == null) return false;

            _mainRenderer.Start(device, latencyMs);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start main renderer: {ex.Message}");
            return false;
        }
    }

    public void StopMainRenderer()
    {
        _mainRenderer.Stop();
    }

    public bool EnableMonitoring(string monitorDeviceId, int latencyMs = 50)
    {
        DisableMonitoring();

        try
        {
            var device = AudioDeviceManager.GetDeviceById(monitorDeviceId);
            if (device == null) return false;

            _monitorJitterBuffer = new CircularJitterBuffer(48000, 1, 16, capacityMs: 500, LatencyMode.Balanced);
            _monitorRenderer = new WasapiAudioRenderer(_monitorJitterBuffer)
            {
                SoundboardMixer = SoundboardMixer,
                IsMonitorOutput = true,
                GainDb = _mainRenderer.GainDb,
                IsMuted = _mainRenderer.IsMuted,
                IsLimiterEnabled = _mainRenderer.IsLimiterEnabled,
                IsHighPassFilterEnabled = _mainRenderer.IsHighPassFilterEnabled,
                IsNoiseGateEnabled = _mainRenderer.IsNoiseGateEnabled,
                NoiseGateThresholdDb = _mainRenderer.NoiseGateThresholdDb,
                IsVoiceChangerEnabled = _mainRenderer.IsVoiceChangerEnabled,
                ActiveVoicePreset = _mainRenderer.ActiveVoicePreset,
                VoicePitchSemitones = _mainRenderer.VoicePitchSemitones
            };

            _monitorRenderer.Start(device, latencyMs);
            _server?.SetMonitorJitterBuffer(_monitorJitterBuffer);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to enable monitoring: {ex.Message}");
            DisableMonitoring();
            return false;
        }
    }

    public void DisableMonitoring()
    {
        _server?.SetMonitorJitterBuffer(null);

        if (_monitorRenderer != null)
        {
            try
            {
                _monitorRenderer.Stop();
                _monitorRenderer.Dispose();
            }
            catch { }
            _monitorRenderer = null;
        }

        _monitorJitterBuffer = null;
    }

    public void SetLatencyMode(LatencyMode mode)
    {
        _mainJitterBuffer.SetLatencyMode(mode);
        _monitorJitterBuffer?.SetLatencyMode(mode);
        int targetDepth = mode switch
        {
            LatencyMode.Low => 40,
            LatencyMode.Stable => 140,
            _ => 80
        };
        _mainRenderer.SetDriftCompensator(new DriftCompensator(_mainJitterBuffer, targetDepth));
    }

    public async Task PlayTestToneAsync(int durationMs = 2000, CancellationToken ct = default)
    {
        await _mainRenderer.PlaySelfTestToneAsync(durationMs, 440.0f, 0.25f, ct).ConfigureAwait(false);
    }

    public static List<string> GetLocalIPv4Addresses()
    {
        var list = new List<string>();
        try
        {
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != OperationalStatus.Up) continue;
                if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var ipProps = iface.GetIPProperties();
                bool hasGateway = ipProps.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));

                foreach (var addr in ipProps.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ipStr = addr.Address.ToString();
                        if (ipStr.StartsWith("169.254.")) continue; // Exclude unconfigured link-local APIPA addresses

                        // Prioritize active network adapter with a default gateway (Wi-Fi / LAN router)
                        if (hasGateway)
                        {
                            list.Insert(0, ipStr);
                        }
                        else
                        {
                            list.Add(ipStr);
                        }
                    }
                }
            }
        }
        catch { }

        if (list.Count == 0)
        {
            list.Add("127.0.0.1");
        }

        return list.Distinct().ToList();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            AdbManager.Dispose();
            DisableMonitoring();
            StopMainRenderer();
            _mainRenderer.Dispose();
            if (_server != null)
            {
                await _server.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
