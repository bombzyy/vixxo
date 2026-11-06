using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using Vixxo.App.Models;
using Vixxo.App.Services;
using Vixxo.Core.Audio;
using Vixxo.Core.Server;
using Vixxo.Core.Usb;

namespace Vixxo.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly AudioService _audioService;
    private readonly SettingsService _settingsService;
    private readonly DispatcherTimer _meterTimer;

    // Server State
    private ServerState _serverState = ServerState.Stopped;
    private string _statusText = "Starting...";
    private string _statusColor = "#94A3B8"; // Slate
    private string _connectedClient = "None";
    private string _connectedClientInfo = "Waiting for phone to connect...";
    private string _primaryIpAddress = "127.0.0.1";
    private int _listenPort = 47800;
    private float _rttMs = 0f;
    private ulong _framesReceived = 0;

    // Devices
    private AudioDeviceInfo? _selectedPlaybackDevice;
    private AudioDeviceInfo? _selectedMonitoringDevice;
    private bool _isVbCableMissing = false;
    private string _vbCableWarningMessage = string.Empty;
    private bool _isMonitoringEnabled = false;

    // Audio Controls
    private float _gainDb = 0f;
    private bool _isMuted = false;
    private bool _isLimiterEnabled = true;
    private bool _isHighPassFilterEnabled = true;
    private bool _isNoiseGateEnabled = true;
    private float _noiseGateThresholdDb = -42.0f;
    private LatencyMode _selectedLatencyMode = LatencyMode.Balanced;

    // Audio Metering
    private float _peakLevel = 0f;
    private float _rmsLevel = 0f;
    private float _peakHoldLevel = 0f;
    private int _peakHoldDecayCounter = 0;
    private string _peakDbText = "-inf dB";
    private float _bufferDepthMs = 0f;

    // Self Test
    private bool _isSelfTesting = false;
    private string _testStatusMessage = string.Empty;

    // USB Mode
    private string _usbStatusText = "Looking for USB Android devices...";
    private string _usbStatusColor = "#94A3B8";

    public MainViewModel(AudioService audioService, SettingsService settingsService)
    {
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

        PlaybackDevices = new ObservableCollection<AudioDeviceInfo>();
        MonitoringDevices = new ObservableCollection<AudioDeviceInfo>();
        LocalIpAddresses = new ObservableCollection<string>();
        ConnectedUsbDevices = new ObservableCollection<AdbDeviceInfo>();

        // Commands
        ToggleMuteCommand = new RelayCommand(ToggleMute);
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        RefreshUsbDevicesCommand = new RelayCommand(RefreshUsbDevices);
        StartSelfTestCommand = new RelayCommand(StartSelfTest, () => !_isSelfTesting);
        DisconnectClientCommand = new RelayCommand(DisconnectClient, () => _serverState == ServerState.Streaming || _serverState == ServerState.ClientConnected);
        ToggleMonitoringCommand = new RelayCommand(ToggleMonitoring);
        OpenPairingDialogCommand = new RelayCommand(OpenPairingDialog);
        InstallDriverCommand = new RelayCommand(async () => await InstallDriverAsync(), () => !_isInstallingDriver);

        // Metering Dispatcher Timer (~30 FPS)
        _meterTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _meterTimer.Tick += OnMeterTick;

        // Initialize from Settings & System
        Initialize();
    }

    // Observable Collections
    public ObservableCollection<AudioDeviceInfo> PlaybackDevices { get; }
    public ObservableCollection<AudioDeviceInfo> MonitoringDevices { get; }
    public ObservableCollection<string> LocalIpAddresses { get; }
    public ObservableCollection<AdbDeviceInfo> ConnectedUsbDevices { get; }

    // Commands
    public ICommand ToggleMuteCommand { get; }
    public ICommand RefreshDevicesCommand { get; }
    public ICommand RefreshUsbDevicesCommand { get; }
    public ICommand StartSelfTestCommand { get; }
    public ICommand DisconnectClientCommand { get; }
    public ICommand ToggleMonitoringCommand { get; }
    public ICommand OpenPairingDialogCommand { get; }
    public ICommand InstallDriverCommand { get; }

    // Properties
    public ServerState ServerState
    {
        get => _serverState;
        private set
        {
            if (SetProperty(ref _serverState, value))
            {
                UpdateStatusDisplay();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string StatusColor
    {
        get => _statusColor;
        private set => SetProperty(ref _statusColor, value);
    }

    public string ConnectedClient
    {
        get => _connectedClient;
        private set => SetProperty(ref _connectedClient, value);
    }

    public string ConnectedClientInfo
    {
        get => _connectedClientInfo;
        private set => SetProperty(ref _connectedClientInfo, value);
    }

    public string PrimaryIpAddress
    {
        get => _primaryIpAddress;
        private set => SetProperty(ref _primaryIpAddress, value);
    }

    public int ListenPort
    {
        get => _listenPort;
        set
        {
            if (SetProperty(ref _listenPort, value))
            {
                _settingsService.CurrentSettings.Port = value;
                _settingsService.Save();
                RestartServer();
            }
        }
    }

    public float RttMs
    {
        get => _rttMs;
        private set => SetProperty(ref _rttMs, value);
    }

    public ulong FramesReceived
    {
        get => _framesReceived;
        private set => SetProperty(ref _framesReceived, value);
    }

    public AudioDeviceInfo? SelectedPlaybackDevice
    {
        get => _selectedPlaybackDevice;
        set
        {
            if (SetProperty(ref _selectedPlaybackDevice, value) && value != null)
            {
                _settingsService.CurrentSettings.SelectedPlaybackDeviceId = value.Id;
                _settingsService.Save();
                _audioService.StartMainRenderer(value.Id);
            }
        }
    }

    public AudioDeviceInfo? SelectedMonitoringDevice
    {
        get => _selectedMonitoringDevice;
        set
        {
            if (SetProperty(ref _selectedMonitoringDevice, value))
            {
                _settingsService.CurrentSettings.SelectedMonitorDeviceId = value?.Id;
                _settingsService.Save();
                if (_isMonitoringEnabled && value != null)
                {
                    _audioService.EnableMonitoring(value.Id);
                }
            }
        }
    }

    public bool IsVbCableMissing
    {
        get => _isVbCableMissing;
        private set => SetProperty(ref _isVbCableMissing, value);
    }

    public string VbCableWarningMessage
    {
        get => _vbCableWarningMessage;
        private set => SetProperty(ref _vbCableWarningMessage, value);
    }

    private bool _isInstallingDriver = false;
    public bool IsInstallingDriver
    {
        get => _isInstallingDriver;
        private set
        {
            if (SetProperty(ref _isInstallingDriver, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private string _driverInstallStatus = "⚡ Install Driver (1-Click)";
    public string DriverInstallStatus
    {
        get => _driverInstallStatus;
        private set => SetProperty(ref _driverInstallStatus, value);
    }

    public bool IsMonitoringEnabled
    {
        get => _isMonitoringEnabled;
        set
        {
            if (SetProperty(ref _isMonitoringEnabled, value))
            {
                _settingsService.CurrentSettings.IsMonitoringEnabled = value;
                _settingsService.Save();
                if (value && _selectedMonitoringDevice != null)
                {
                    _audioService.EnableMonitoring(_selectedMonitoringDevice.Id);
                }
                else
                {
                    _audioService.DisableMonitoring();
                }
            }
        }
    }

    public float GainDb
    {
        get => _gainDb;
        set
        {
            if (SetProperty(ref _gainDb, value))
            {
                _audioService.MainRenderer.GainDb = value;
                if (_audioService.MonitorRenderer != null)
                {
                    _audioService.MonitorRenderer.GainDb = value;
                }
                _settingsService.CurrentSettings.GainDb = value;
                _settingsService.Save();
                OnPropertyChanged(nameof(GainText));
            }
        }
    }

    public string GainText => $"{GainDb:+0.0;-0.0;0.0} dB";

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
            {
                _audioService.MainRenderer.IsMuted = value;
                if (_audioService.MonitorRenderer != null)
                {
                    _audioService.MonitorRenderer.IsMuted = value;
                }
                _settingsService.CurrentSettings.IsMuted = value;
                _settingsService.Save();
                OnPropertyChanged(nameof(MuteButtonText));
                OnPropertyChanged(nameof(MuteButtonColor));
            }
        }
    }

    public string MuteButtonText => IsMuted ? "UNMUTE MIC" : "MUTE MIC";
    public string MuteButtonColor => IsMuted ? "#EF4444" : "#38BDF8";

    public bool IsLimiterEnabled
    {
        get => _isLimiterEnabled;
        set
        {
            if (SetProperty(ref _isLimiterEnabled, value))
            {
                _audioService.MainRenderer.IsLimiterEnabled = value;
                if (_audioService.MonitorRenderer != null)
                {
                    _audioService.MonitorRenderer.IsLimiterEnabled = value;
                }
                _settingsService.CurrentSettings.IsLimiterEnabled = value;
                _settingsService.Save();
            }
        }
    }

    public bool IsHighPassFilterEnabled
    {
        get => _isHighPassFilterEnabled;
        set
        {
            if (SetProperty(ref _isHighPassFilterEnabled, value))
            {
                _audioService.MainRenderer.IsHighPassFilterEnabled = value;
                if (_audioService.MonitorRenderer != null)
                {
                    _audioService.MonitorRenderer.IsHighPassFilterEnabled = value;
                }
                _settingsService.CurrentSettings.IsHighPassFilterEnabled = value;
                _settingsService.Save();
            }
        }
    }

    public bool IsNoiseGateEnabled
    {
        get => _isNoiseGateEnabled;
        set
        {
            if (SetProperty(ref _isNoiseGateEnabled, value))
            {
                _audioService.MainRenderer.IsNoiseGateEnabled = value;
                if (_audioService.MonitorRenderer != null)
                {
                    _audioService.MonitorRenderer.IsNoiseGateEnabled = value;
                }
                _settingsService.CurrentSettings.IsNoiseGateEnabled = value;
                _settingsService.Save();
            }
        }
    }

    public float NoiseGateThresholdDb
    {
        get => _noiseGateThresholdDb;
        set
        {
            if (SetProperty(ref _noiseGateThresholdDb, value))
            {
                _audioService.MainRenderer.NoiseGateThresholdDb = value;
                if (_audioService.MonitorRenderer != null)
                {
                    _audioService.MonitorRenderer.NoiseGateThresholdDb = value;
                }
                _settingsService.CurrentSettings.NoiseGateThresholdDb = value;
                _settingsService.Save();
                OnPropertyChanged(nameof(NoiseGateThresholdText));
            }
        }
    }

    public string NoiseGateThresholdText => $"{NoiseGateThresholdDb:0.0} dB";

    public LatencyMode SelectedLatencyMode
    {
        get => _selectedLatencyMode;
        set
        {
            if (SetProperty(ref _selectedLatencyMode, value))
            {
                _audioService.SetLatencyMode(value);
                _settingsService.CurrentSettings.LatencyMode = value;
                _settingsService.Save();
            }
        }
    }

    public float PeakLevel
    {
        get => _peakLevel;
        private set => SetProperty(ref _peakLevel, value);
    }

    public float RmsLevel
    {
        get => _rmsLevel;
        private set => SetProperty(ref _rmsLevel, value);
    }

    public float PeakHoldLevel
    {
        get => _peakHoldLevel;
        private set => SetProperty(ref _peakHoldLevel, value);
    }

    public string PeakDbText
    {
        get => _peakDbText;
        private set => SetProperty(ref _peakDbText, value);
    }

    public float BufferDepthMs
    {
        get => _bufferDepthMs;
        private set => SetProperty(ref _bufferDepthMs, value);
    }

    public bool IsSelfTesting
    {
        get => _isSelfTesting;
        private set => SetProperty(ref _isSelfTesting, value);
    }

    public string TestStatusMessage
    {
        get => _testStatusMessage;
        private set => SetProperty(ref _testStatusMessage, value);
    }

    public string UsbStatusText
    {
        get => _usbStatusText;
        private set => SetProperty(ref _usbStatusText, value);
    }

    public string UsbStatusColor
    {
        get => _usbStatusColor;
        private set => SetProperty(ref _usbStatusColor, value);
    }

    private void Initialize()
    {
        _settingsService.Load();
        var s = _settingsService.CurrentSettings;

        _listenPort = s.Port;
        _gainDb = s.GainDb;
        _isMuted = s.IsMuted;
        _isLimiterEnabled = s.IsLimiterEnabled;
        _isHighPassFilterEnabled = s.IsHighPassFilterEnabled;
        _isNoiseGateEnabled = s.IsNoiseGateEnabled;
        _noiseGateThresholdDb = s.NoiseGateThresholdDb;
        _selectedLatencyMode = s.LatencyMode;
        _isMonitoringEnabled = s.IsMonitoringEnabled;

        // Apply audio states
        _audioService.MainRenderer.GainDb = _gainDb;
        _audioService.MainRenderer.IsMuted = _isMuted;
        _audioService.MainRenderer.IsLimiterEnabled = _isLimiterEnabled;
        _audioService.MainRenderer.IsHighPassFilterEnabled = _isHighPassFilterEnabled;
        _audioService.MainRenderer.IsNoiseGateEnabled = _isNoiseGateEnabled;
        _audioService.MainRenderer.NoiseGateThresholdDb = _noiseGateThresholdDb;
        _audioService.SetLatencyMode(_selectedLatencyMode);

        // Load network IPs
        LocalIpAddresses.Clear();
        var ips = AudioService.GetLocalIPv4Addresses();
        foreach (var ip in ips) LocalIpAddresses.Add(ip);
        PrimaryIpAddress = ips.FirstOrDefault() ?? "127.0.0.1";

        // Refresh devices and bind CABLE Input
        RefreshDevices();

        // Start Server
        RestartServer();

        // Start metering timer
        _meterTimer.Start();
    }

    public void RefreshDevices()
    {
        PlaybackDevices.Clear();
        MonitoringDevices.Clear();

        var playbacks = AudioDeviceManager.GetPlaybackDevices();
        var recordings = AudioDeviceManager.GetRecordingDevices();

        // Check VB-CABLE playback
        var vbCablePlayback = AudioDeviceManager.FindVbCablePlaybackDevice();
        var vbCableRecording = AudioDeviceManager.FindVbCableRecordingDevice();

        if (vbCablePlayback == null || vbCableRecording == null)
        {
            IsVbCableMissing = true;
            VbCableWarningMessage = "VB-CABLE driver is not detected on your system. Please install VB-CABLE Driver to enable virtual microphone routing.";
        }
        else
        {
            IsVbCableMissing = false;
            VbCableWarningMessage = string.Empty;
        }

        AudioDeviceInfo? deviceToSelect = null;
        string? savedId = _settingsService.CurrentSettings.SelectedPlaybackDeviceId;

        foreach (var d in playbacks)
        {
            PlaybackDevices.Add(d);
            // Non-cable devices available for local monitoring
            if (!d.IsVbCable)
            {
                MonitoringDevices.Add(d);
            }

            if (savedId != null && string.Equals(d.Id, savedId, StringComparison.OrdinalIgnoreCase))
            {
                deviceToSelect = d;
            }
        }

        // Default to CABLE Input if saved ID not found
        if (deviceToSelect == null && vbCablePlayback != null)
        {
            deviceToSelect = vbCablePlayback;
        }

        SelectedPlaybackDevice = deviceToSelect ?? playbacks.FirstOrDefault();

        // Restore monitoring device if saved
        string? savedMonitorId = _settingsService.CurrentSettings.SelectedMonitorDeviceId;
        if (savedMonitorId != null)
        {
            SelectedMonitoringDevice = MonitoringDevices.FirstOrDefault(m => string.Equals(m.Id, savedMonitorId, StringComparison.OrdinalIgnoreCase));
        }
        if (SelectedMonitoringDevice == null)
        {
            SelectedMonitoringDevice = MonitoringDevices.FirstOrDefault(m => m.IsDefault) ?? MonitoringDevices.FirstOrDefault();
        }
    }

    private void RestartServer()
    {
        _audioService.StopServer();
        _audioService.StartServer(_listenPort);

        if (_audioService.Server != null)
        {
            _audioService.Server.StateChanged += OnServerStateChanged;
            _audioService.Server.ClientConnected += OnClientConnected;
            _audioService.Server.ClientDisconnected += OnClientDisconnected;
            _audioService.Server.AudioFrameReceived += OnAudioFrameReceived;
            ServerState = _audioService.Server.State;
        }

        if (_audioService.AdbWatcher != null)
        {
            _audioService.AdbWatcher.DevicesChanged += OnAdbDevicesChanged;
        }

        RefreshUsbDevices();
    }

    private void OnServerStateChanged(ServerState state)
    {
        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            ServerState = state;
        });
    }

    private void OnClientConnected(string clientName)
    {
        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            ConnectedClient = clientName;
            ConnectedClientInfo = $"Connected to '{clientName}' via TCP Port {ListenPort}";
        });
    }

    private void OnClientDisconnected(string reason)
    {
        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            ConnectedClient = "None";
            ConnectedClientInfo = $"Client disconnected ({reason}). Waiting for connection...";
            RttMs = 0f;
            FramesReceived = 0;
        });
    }

    private void OnAudioFrameReceived(ulong seq, int bytes)
    {
        // Periodic frame update
        if (seq % 25 == 0)
        {
            App.Current?.Dispatcher.InvokeAsync(() =>
            {
                FramesReceived = seq;
                if (_audioService.Server != null)
                {
                    RttMs = _audioService.Server.LastRttMs;
                }
            });
        }
    }

    private void OnMeterTick(object? sender, EventArgs e)
    {
        float currentPeak = _audioService.MainRenderer.PeakLevel;
        float currentRms = _audioService.MainRenderer.RmsLevel;

        PeakLevel = currentPeak;
        RmsLevel = currentRms;
        BufferDepthMs = _audioService.MainRenderer.CurrentBufferDepthMs;

        // Peak Hold logic
        if (currentPeak >= _peakHoldLevel)
        {
            _peakHoldLevel = currentPeak;
            _peakHoldDecayCounter = 15; // Hold for 15 ticks (~500ms)
        }
        else
        {
            if (_peakHoldDecayCounter > 0)
            {
                _peakHoldDecayCounter--;
            }
            else
            {
                _peakHoldLevel = Math.Max(0f, _peakHoldLevel - 0.05f); // Smooth decay
            }
        }
        PeakHoldLevel = _peakHoldLevel;

        // Format dB string
        if (currentPeak > 0.0001f)
        {
            float db = (float)(20.0 * Math.Log10(currentPeak));
            PeakDbText = $"{db:0.0} dB";
        }
        else
        {
            PeakDbText = "-inf dB";
        }
    }

    private void UpdateStatusDisplay()
    {
        switch (_serverState)
        {
            case ServerState.Stopped:
                StatusText = "STOPPED";
                StatusColor = "#EF4444"; // Red
                break;
            case ServerState.Listening:
                StatusText = "LISTENING";
                StatusColor = "#38BDF8"; // Cyan
                break;
            case ServerState.ClientConnected:
                StatusText = "AUTHENTICATED";
                StatusColor = "#F59E0B"; // Amber
                break;
            case ServerState.Streaming:
                StatusText = "STREAMING LIVE";
                StatusColor = "#22C55E"; // Green
                break;
        }
    }

    private void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    private void ToggleMonitoring()
    {
        IsMonitoringEnabled = !IsMonitoringEnabled;
    }

    private void DisconnectClient()
    {
        RestartServer();
    }

    private void OpenPairingDialog()
    {
        var dialog = new Views.PairingDialog(_audioService.SecurityManager, PrimaryIpAddress, ListenPort)
        {
            Owner = App.Current?.MainWindow
        };
        dialog.ShowDialog();
    }

    private async Task InstallDriverAsync()
    {
        if (IsInstallingDriver) return;
        IsInstallingDriver = true;
        DriverInstallStatus = "Installing driver...";

        var progress = new Progress<string>(msg =>
        {
            DriverInstallStatus = msg;
        });

        bool success = await DriverInstaller.InstallVbCableAsync(progress).ConfigureAwait(true);

        RefreshDevices();

        if (!IsVbCableMissing)
        {
            DriverInstallStatus = "✅ Driver Ready!";
            System.Windows.MessageBox.Show(
                "VB-CABLE Virtual Microphone Driver installed successfully!\n\nVixxo is now ready to route audio to Discord, Zoom, Teams, and OBS.",
                "Driver Installed",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        else
        {
            DriverInstallStatus = "⚡ Retry 1-Click Install";
            if (!success)
            {
                System.Windows.MessageBox.Show(
                    "Driver installation was not completed. Please run Vixxo as Administrator or install manually.",
                    "Installation Incomplete",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        IsInstallingDriver = false;
    }

    private async void StartSelfTest()
    {
        if (IsSelfTesting) return;

        IsSelfTesting = true;
        TestStatusMessage = "Playing 440 Hz test tone into CABLE Input (2 sec)... Check CABLE Output in Sound Recorder / Discord!";

        try
        {
            await _audioService.PlayTestToneAsync(2000);
            TestStatusMessage = "Self-test tone finished. If you saw the meter move, CABLE Input routing is working correctly.";
        }
        catch (Exception ex)
        {
            TestStatusMessage = $"Self-test failed: {ex.Message}";
        }
        finally
        {
            IsSelfTesting = false;
        }
    }

    private async void RefreshUsbDevices()
    {
        if (!_audioService.AdbManager.IsAdbAvailable)
        {
            UsbStatusText = "ADB not detected on PC. Install Android SDK Platform-Tools.";
            UsbStatusColor = "#EF4444";
            return;
        }

        var devices = await _audioService.AdbManager.GetConnectedDevicesAsync();
        OnAdbDevicesChanged(devices);
    }

    private void OnAdbDevicesChanged(List<AdbDeviceInfo> devices)
    {
        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            ConnectedUsbDevices.Clear();
            foreach (var d in devices) ConnectedUsbDevices.Add(d);

            var ready = devices.FirstOrDefault(d => d.IsReady);
            if (ready != null)
            {
                UsbStatusText = $"USB Ready: {ready.Model} ({ready.Serial}) mapped to 127.0.0.1:{ListenPort}";
                UsbStatusColor = "#22C55E";
            }
            else if (devices.Any(d => d.State == "unauthorized"))
            {
                UsbStatusText = "⚠️ USB device detected! Tap 'Allow USB Debugging' on your phone.";
                UsbStatusColor = "#F59E0B";
            }
            else
            {
                UsbStatusText = "No USB devices. Connect phone with USB Debugging enabled.";
                UsbStatusColor = "#94A3B8";
            }
        });
    }

    public void Dispose()
    {
        _meterTimer.Stop();
        _meterTimer.Tick -= OnMeterTick;
        _audioService.DisposeAsync().GetAwaiter().GetResult();
    }
}
