using System.Collections.ObjectModel;
using System.IO;
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

    // Workspace Navigation
    private string _currentTab = "Studio";

    // Soundboard
    private float _soundboardVolumeDb = -3.0f;
    private float _micVolumeDb = 0.0f;
    private bool _sendClipsToVirtualMic = true;
    private bool _monitorClipsInHeadphones = false;
    private string _nowPlayingText = "Nothing playing";
    private string _searchSoundText = string.Empty;
    private string _selectedCategory = "All sounds";

    // Real-Time Voice Changer
    private bool _isVoiceChangerEnabled = false;
    private VoiceEngineMode _selectedEngineMode = VoiceEngineMode.AiNeural;
    private VoicePreset _activeVoicePreset = VoicePreset.Bypass;
    private float _voicePitchSemitones = 0.0f;
    private float _formantShift = 0.0f;
    private float _indexRate = 0.75f;

    public MainViewModel(AudioService audioService, SettingsService settingsService)
    {
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

        PlaybackDevices = new ObservableCollection<AudioDeviceInfo>();
        MonitoringDevices = new ObservableCollection<AudioDeviceInfo>();
        LocalIpAddresses = new ObservableCollection<string>();
        ConnectedUsbDevices = new ObservableCollection<AdbDeviceInfo>();
        SoundPads = new ObservableCollection<SoundPadViewModel>();
        FilteredSoundPads = new ObservableCollection<SoundPadViewModel>();
        Categories = new ObservableCollection<string> { "All sounds", "Valorant", "Memes", "Custom" };
        VoiceModelCards = new ObservableCollection<VoiceModelCardViewModel>();

        // Commands
        ToggleMuteCommand = new RelayCommand(ToggleMute);
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        RefreshUsbDevicesCommand = new RelayCommand(RefreshUsbDevices);
        StartSelfTestCommand = new RelayCommand(StartSelfTest, () => !_isSelfTesting);
        DisconnectClientCommand = new RelayCommand(DisconnectClient, () => _serverState == ServerState.Streaming || _serverState == ServerState.ClientConnected);
        ToggleMonitoringCommand = new RelayCommand(ToggleMonitoring);
        OpenPairingDialogCommand = new RelayCommand(OpenPairingDialog);
        InstallDriverCommand = new RelayCommand(async () => await InstallDriverAsync(), () => !_isInstallingDriver);

        // Navigation Commands
        SelectStudioCommand = new RelayCommand(() => CurrentTab = "Studio");
        SelectSoundboardCommand = new RelayCommand(() => CurrentTab = "Soundboard");
        SelectVoiceChangerCommand = new RelayCommand(() => CurrentTab = "VoiceChanger");
        SelectSettingsCommand = new RelayCommand(() => CurrentTab = "Settings");

        // Soundboard Commands
        StopAllSoundsCommand = new RelayCommand(StopAllSounds);
        AddSoundCommand = new RelayCommand(AddCustomSound);

        // Voice Changer Commands
        SelectAiEngineCommand = new RelayCommand(() => SelectedEngineMode = VoiceEngineMode.AiNeural);
        SelectFastDspCommand = new RelayCommand(() => SelectedEngineMode = VoiceEngineMode.FastDsp);
        ImportCustomModelCommand = new RelayCommand(ImportCustomModel);
        SelectGirlPresetCommand = new RelayCommand(() => { ActiveVoicePreset = VoicePreset.Girl; IsVoiceChangerEnabled = true; });
        SelectLadyPresetCommand = new RelayCommand(() => { ActiveVoicePreset = VoicePreset.Lady; IsVoiceChangerEnabled = true; });
        SelectDemonPresetCommand = new RelayCommand(() => { ActiveVoicePreset = VoicePreset.Demon; IsVoiceChangerEnabled = true; });
        SelectHeliumPresetCommand = new RelayCommand(() => { ActiveVoicePreset = VoicePreset.Helium; IsVoiceChangerEnabled = true; });
        SelectRobotPresetCommand = new RelayCommand(() => { ActiveVoicePreset = VoicePreset.Robot; IsVoiceChangerEnabled = true; });
        SelectRadioPresetCommand = new RelayCommand(() => { ActiveVoicePreset = VoicePreset.Radio; IsVoiceChangerEnabled = true; });
        SelectEchoPresetCommand = new RelayCommand(() => { ActiveVoicePreset = VoicePreset.Echo; IsVoiceChangerEnabled = true; });
        SelectBypassPresetCommand = new RelayCommand(() => { ActiveVoicePreset = VoicePreset.Bypass; IsVoiceChangerEnabled = false; });

        // Initialize Voice Model Cards
        InitializeVoiceModelCards();

        // Listen for Soundboard playback events
        _audioService.SoundboardMixer.PlaybackStateChanged += OnSoundboardStateChanged;

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
    public ObservableCollection<SoundPadViewModel> SoundPads { get; }
    public ObservableCollection<SoundPadViewModel> FilteredSoundPads { get; }
    public ObservableCollection<string> Categories { get; }
    public ObservableCollection<VoiceModelCardViewModel> VoiceModelCards { get; }

    // Commands
    public ICommand ToggleMuteCommand { get; }
    public ICommand RefreshDevicesCommand { get; }
    public ICommand RefreshUsbDevicesCommand { get; }
    public ICommand StartSelfTestCommand { get; }
    public ICommand DisconnectClientCommand { get; }
    public ICommand ToggleMonitoringCommand { get; }
    public ICommand OpenPairingDialogCommand { get; }
    public ICommand InstallDriverCommand { get; }

    // Navigation Commands
    public ICommand SelectStudioCommand { get; }
    public ICommand SelectSoundboardCommand { get; }
    public ICommand SelectVoiceChangerCommand { get; }
    public ICommand SelectSettingsCommand { get; }

    // Soundboard Commands
    public ICommand StopAllSoundsCommand { get; }
    public ICommand AddSoundCommand { get; }

    // Voice Changer Commands
    public ICommand SelectAiEngineCommand { get; }
    public ICommand SelectFastDspCommand { get; }
    public ICommand ImportCustomModelCommand { get; }
    public ICommand SelectGirlPresetCommand { get; }
    public ICommand SelectLadyPresetCommand { get; }
    public ICommand SelectDemonPresetCommand { get; }
    public ICommand SelectHeliumPresetCommand { get; }
    public ICommand SelectRobotPresetCommand { get; }
    public ICommand SelectRadioPresetCommand { get; }
    public ICommand SelectEchoPresetCommand { get; }
    public ICommand SelectBypassPresetCommand { get; }

    // Navigation Properties
    public string CurrentTab
    {
        get => _currentTab;
        set
        {
            if (SetProperty(ref _currentTab, value))
            {
                OnPropertyChanged(nameof(IsStudioTabActive));
                OnPropertyChanged(nameof(IsSoundboardTabActive));
                OnPropertyChanged(nameof(IsVoiceChangerTabActive));
                OnPropertyChanged(nameof(IsSettingsTabActive));
                OnPropertyChanged(nameof(BreadcrumbText));
            }
        }
    }

    public bool IsStudioTabActive => CurrentTab == "Studio";
    public bool IsSoundboardTabActive => CurrentTab == "Soundboard";
    public bool IsVoiceChangerTabActive => CurrentTab == "VoiceChanger";
    public bool IsSettingsTabActive => CurrentTab == "Settings";
    public string BreadcrumbText => $"VIXXO  ›  {(CurrentTab == "VoiceChanger" ? "Voice Changer" : CurrentTab)}";

    // Soundboard Properties
    public float SoundboardVolumeDb
    {
        get => _soundboardVolumeDb;
        set
        {
            if (SetProperty(ref _soundboardVolumeDb, value))
            {
                _audioService.SoundboardMixer.SoundboardVolumeDb = value;
                _settingsService.CurrentSettings.SoundboardVolumeDb = value;
                OnPropertyChanged(nameof(SoundboardVolumeText));
            }
        }
    }

    public string SoundboardVolumeText => $"{SoundboardVolumeDb:F1} dB";

    public float MicVolumeDb
    {
        get => _micVolumeDb;
        set
        {
            if (SetProperty(ref _micVolumeDb, value))
            {
                _audioService.SoundboardMixer.MicrophoneVolumeDb = value;
                _settingsService.CurrentSettings.MicVolumeDb = value;
                OnPropertyChanged(nameof(MicVolumeText));
            }
        }
    }

    public string MicVolumeText => $"{MicVolumeDb:F1} dB";

    public bool SendClipsToVirtualMic
    {
        get => _sendClipsToVirtualMic;
        set
        {
            if (SetProperty(ref _sendClipsToVirtualMic, value))
            {
                _audioService.SoundboardMixer.SendClipsToVirtualMic = value;
                _settingsService.CurrentSettings.SendClipsToVirtualMic = value;
            }
        }
    }

    public bool MonitorClipsInHeadphones
    {
        get => _monitorClipsInHeadphones;
        set
        {
            if (SetProperty(ref _monitorClipsInHeadphones, value))
            {
                _audioService.SoundboardMixer.MonitorClipsInHeadphones = value;
                _settingsService.CurrentSettings.MonitorClipsInHeadphones = value;
            }
        }
    }

    public string NowPlayingText
    {
        get => _nowPlayingText;
        private set => SetProperty(ref _nowPlayingText, value);
    }

    public string SoundClipsCountText => $"{_audioService.SoundboardMixer.Clips.Count} clips";

    public string SearchSoundText
    {
        get => _searchSoundText;
        set
        {
            if (SetProperty(ref _searchSoundText, value))
            {
                ApplySoundFilter();
            }
        }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                ApplySoundFilter();
            }
        }
    }

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

    // Real-Time Voice Changer Properties
    public bool IsVoiceChangerEnabled
    {
        get => _isVoiceChangerEnabled;
        set
        {
            if (SetProperty(ref _isVoiceChangerEnabled, value))
            {
                _audioService.MainRenderer.IsVoiceChangerEnabled = value;
                if (_audioService.MonitorRenderer != null)
                {
                    _audioService.MonitorRenderer.IsVoiceChangerEnabled = value;
                }
                _settingsService.CurrentSettings.IsVoiceChangerEnabled = value;
                _settingsService.Save();
                OnPropertyChanged(nameof(VoiceChangerStatusText));
                OnPropertyChanged(nameof(IsGirlActive));
                OnPropertyChanged(nameof(IsLadyActive));
                OnPropertyChanged(nameof(IsDemonActive));
                OnPropertyChanged(nameof(IsHeliumActive));
                OnPropertyChanged(nameof(IsRobotActive));
                OnPropertyChanged(nameof(IsRadioActive));
                OnPropertyChanged(nameof(IsEchoActive));
                OnPropertyChanged(nameof(IsBypassActive));
            }
        }
    }

    public VoicePreset ActiveVoicePreset
    {
        get => _activeVoicePreset;
        set
        {
            if (SetProperty(ref _activeVoicePreset, value))
            {
                _audioService.MainRenderer.ActiveVoicePreset = value;
                if (_audioService.MonitorRenderer != null)
                {
                    _audioService.MonitorRenderer.ActiveVoicePreset = value;
                }
                _settingsService.CurrentSettings.ActiveVoicePreset = value;
                _settingsService.Save();

                _voicePitchSemitones = _audioService.MainRenderer.VoicePitchSemitones;
                OnPropertyChanged(nameof(VoicePitchSemitones));
                OnPropertyChanged(nameof(VoicePitchText));
                OnPropertyChanged(nameof(VoiceChangerStatusText));
                OnPropertyChanged(nameof(IsGirlActive));
                OnPropertyChanged(nameof(IsLadyActive));
                OnPropertyChanged(nameof(IsDemonActive));
                OnPropertyChanged(nameof(IsHeliumActive));
                OnPropertyChanged(nameof(IsRobotActive));
                OnPropertyChanged(nameof(IsRadioActive));
                OnPropertyChanged(nameof(IsEchoActive));
                OnPropertyChanged(nameof(IsBypassActive));
            }
        }
    }

    public float VoicePitchSemitones
    {
        get => _voicePitchSemitones;
        set
        {
            if (SetProperty(ref _voicePitchSemitones, value))
            {
                _audioService.MainRenderer.VoicePitchSemitones = value;
                if (_audioService.MonitorRenderer != null)
                {
                    _audioService.MonitorRenderer.VoicePitchSemitones = value;
                }
                _settingsService.CurrentSettings.VoicePitchSemitones = value;
                _settingsService.Save();
                OnPropertyChanged(nameof(VoicePitchText));
            }
        }
    }

    public string VoicePitchText => $"{VoicePitchSemitones:+0.0;-0.0;0.0} st";
    public string VoiceChangerStatusText => IsVoiceChangerEnabled ? $"Active: {ActiveVoicePreset}" : "Voice Changer Off";

    public bool IsGirlActive => IsVoiceChangerEnabled && ActiveVoicePreset == VoicePreset.Girl;
    public bool IsLadyActive => IsVoiceChangerEnabled && ActiveVoicePreset == VoicePreset.Lady;
    public bool IsDemonActive => IsVoiceChangerEnabled && ActiveVoicePreset == VoicePreset.Demon;
    public bool IsHeliumActive => IsVoiceChangerEnabled && ActiveVoicePreset == VoicePreset.Helium;
    public bool IsRobotActive => IsVoiceChangerEnabled && ActiveVoicePreset == VoicePreset.Robot;
    public bool IsRadioActive => IsVoiceChangerEnabled && ActiveVoicePreset == VoicePreset.Radio;
    public bool IsEchoActive => IsVoiceChangerEnabled && ActiveVoicePreset == VoicePreset.Echo;
    public bool IsBypassActive => !IsVoiceChangerEnabled || ActiveVoicePreset == VoicePreset.Bypass;

    // AI Neural Engine & Voice Changer Workspace Properties
    public VoiceEngineMode SelectedEngineMode
    {
        get => _selectedEngineMode;
        set
        {
            if (SetProperty(ref _selectedEngineMode, value))
            {
                _audioService.MainRenderer.VoiceEngineMode = value;
                if (_audioService.MonitorRenderer != null) _audioService.MonitorRenderer.VoiceEngineMode = value;
                _settingsService.CurrentSettings.VoiceEngineMode = value;
                _settingsService.Save();
                OnPropertyChanged(nameof(IsAiEngineActive));
                OnPropertyChanged(nameof(IsFastDspActive));
                OnPropertyChanged(nameof(EngineModeTitleText));
                OnPropertyChanged(nameof(GpuLatencyText));
            }
        }
    }

    public bool IsAiEngineActive => SelectedEngineMode == VoiceEngineMode.AiNeural;
    public bool IsFastDspActive => SelectedEngineMode == VoiceEngineMode.FastDsp;
    public string EngineModeTitleText => IsAiEngineActive ? "AI Neural Engine (RVC v2 • RTX 4050)" : "Classic Fast DSP Engine (< 5ms)";
    public string GpuHardwareText => "NVIDIA GeForce RTX 4050 (Active • DirectML/CUDA)";
    public string GpuLatencyText => IsAiEngineActive ? "~42 ms (Tensor Cores)" : "< 5 ms (Zero-Lag DSP)";

    public float FormantShift
    {
        get => _formantShift;
        set
        {
            if (SetProperty(ref _formantShift, value))
            {
                _audioService.MainRenderer.FormantShift = value;
                if (_audioService.MonitorRenderer != null) _audioService.MonitorRenderer.FormantShift = value;
                _settingsService.CurrentSettings.FormantShift = value;
                _settingsService.Save();
                OnPropertyChanged(nameof(FormantShiftText));
            }
        }
    }
    public string FormantShiftText => $"{FormantShift:+0.0;-0.0;0.0}";

    public float IndexRate
    {
        get => _indexRate;
        set
        {
            if (SetProperty(ref _indexRate, value))
            {
                _audioService.MainRenderer.IndexRate = value;
                if (_audioService.MonitorRenderer != null) _audioService.MonitorRenderer.IndexRate = value;
                _settingsService.CurrentSettings.IndexRate = value;
                _settingsService.Save();
                OnPropertyChanged(nameof(IndexRateText));
            }
        }
    }
    public string IndexRateText => $"{IndexRate:P0}";

    public void InitializeVoiceModelCards()
    {
        VoiceModelCards.Clear();

        var models = new List<(string Id, string Name, string Subtitle, string Desc, string Emoji, string Tag, string TagColor, VoicePreset Preset, float Pitch, float Formant)>
        {
            ("girl", "Sweet Anime Girl", "Kawaii VTuber", "High clarity youthful female voice with tuned vocal tract formants", "👧", "POPULAR", "#E879F9", VoicePreset.Girl, 5.8f, 2.0f),
            ("lady", "Mature Lady", "Natural Adult Female", "Warm, confident adult female vocal timbre for gaming comms & Discord", "👩", "NATURAL", "#A855F7", VoicePreset.Lady, 3.8f, 1.2f),
            ("sage", "Valorant Sage", "Tactical Healer", "Calm, assertive tactical leader voice for team calls", "🧝", "VALORANT", "#10B981", VoicePreset.Sage, 3.5f, 1.0f),
            ("jett", "Valorant Jett", "Wind Assassin", "Sharp, energetic duelist vocal tone for clutch trolling", "💨", "VALORANT", "#38BDF8", VoicePreset.Jett, 5.2f, 1.8f),
            ("demon", "Shadow Demon", "Overlord Boss", "Deep demonic pitch-down with dark sub-bass overdrive", "👹", "DARK", "#EF4444", VoicePreset.Demon, -4.8f, -2.5f),
            ("helium", "Helium Squeak", "Cartoon Troll", "Extreme high squeaky pitch for funny gaming moments", "🎈", "TROLL", "#F59E0B", VoicePreset.Helium, 8.5f, 3.0f),
            ("robot", "Cyborg RingMod", "Metallic Vocoder", "55 Hz robotic ring modulator carrier effect", "🤖", "EFFECT", "#06B6D4", VoicePreset.Robot, 0.0f, 0.0f),
            ("radio", "Military Radio", "Walkie-Talkie", "350Hz-3400Hz tactical comms bandpass + AM overdrive crunch", "📻", "EFFECT", "#10B981", VoicePreset.Radio, 0.5f, 0.0f),
            ("echo", "Stadium Echo", "Cathedral Reverb", "Spacious dual comb reverb for booming announcements", "🏛️", "EFFECT", "#3B82F6", VoicePreset.Echo, 0.0f, 0.0f),
            ("bypass", "Clean Mic", "Original Audio", "Direct un-altered microphone passthrough", "🎙️", "DEFAULT", "#64748B", VoicePreset.Bypass, 0.0f, 0.0f)
        };

        foreach (var m in models)
        {
            var card = new VoiceModelCardViewModel
            {
                Id = m.Id,
                Name = m.Name,
                Subtitle = m.Subtitle,
                Description = m.Desc,
                AvatarEmoji = m.Emoji,
                Tag = m.Tag,
                TagColor = m.TagColor,
                Preset = m.Preset,
                DefaultPitch = m.Pitch,
                DefaultFormant = m.Formant,
                IsSelected = (m.Preset == _activeVoicePreset)
            };
            card.SelectCommand = new RelayCommand(() => SelectVoiceModel(card));
            VoiceModelCards.Add(card);
        }
    }

    public void SelectVoiceModel(VoiceModelCardViewModel card)
    {
        if (card == null) return;
        foreach (var c in VoiceModelCards)
        {
            c.IsSelected = (c == card);
        }
        ActiveVoicePreset = card.Preset;
        VoicePitchSemitones = card.DefaultPitch;
        FormantShift = card.DefaultFormant;
        if (card.IsCustom && !string.IsNullOrEmpty(card.FilePath))
        {
            _audioService.MainRenderer.CustomModelPath = card.FilePath;
            _settingsService.CurrentSettings.CustomModelPath = card.FilePath;
            _settingsService.Save();
        }
        IsVoiceChangerEnabled = true;
    }

    private void ImportCustomModel()
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select RVC Voice Model (.pth or .onnx)",
                Filter = "RVC Voice Models (*.pth;*.onnx)|*.pth;*.onnx|All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
                var card = new VoiceModelCardViewModel
                {
                    Id = $"custom_{Guid.NewGuid():N}",
                    Name = fileName,
                    Subtitle = "Imported RVC Model",
                    Description = $"Custom voice model loaded from {dlg.FileName}",
                    AvatarEmoji = "⭐",
                    Tag = "CUSTOM",
                    TagColor = "#F59E0B",
                    Preset = VoicePreset.Bypass,
                    DefaultPitch = 0.0f,
                    DefaultFormant = 0.0f,
                    IsCustom = true,
                    FilePath = dlg.FileName
                };
                card.SelectCommand = new RelayCommand(() => SelectVoiceModel(card));
                VoiceModelCards.Add(card);
                SelectVoiceModel(card);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to import custom model: {ex.Message}");
        }
    }

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
        _soundboardVolumeDb = s.SoundboardVolumeDb;
        _micVolumeDb = s.MicVolumeDb;
        _sendClipsToVirtualMic = s.SendClipsToVirtualMic;
        _monitorClipsInHeadphones = s.MonitorClipsInHeadphones;
        _isVoiceChangerEnabled = s.IsVoiceChangerEnabled;
        _activeVoicePreset = s.ActiveVoicePreset;
        _voicePitchSemitones = s.VoicePitchSemitones;
        _selectedEngineMode = s.VoiceEngineMode;
        _formantShift = s.FormantShift;
        _indexRate = s.IndexRate;

        // Apply audio states
        _audioService.MainRenderer.GainDb = _gainDb;
        _audioService.MainRenderer.IsMuted = _isMuted;
        _audioService.MainRenderer.IsLimiterEnabled = _isLimiterEnabled;
        _audioService.MainRenderer.IsHighPassFilterEnabled = _isHighPassFilterEnabled;
        _audioService.MainRenderer.IsNoiseGateEnabled = _isNoiseGateEnabled;
        _audioService.MainRenderer.NoiseGateThresholdDb = _noiseGateThresholdDb;
        _audioService.MainRenderer.IsVoiceChangerEnabled = _isVoiceChangerEnabled;
        _audioService.MainRenderer.VoiceEngineMode = _selectedEngineMode;
        _audioService.MainRenderer.ActiveVoicePreset = _activeVoicePreset;
        _audioService.MainRenderer.VoicePitchSemitones = _voicePitchSemitones;
        _audioService.MainRenderer.FormantShift = _formantShift;
        _audioService.MainRenderer.IndexRate = _indexRate;
        _audioService.MainRenderer.CustomModelPath = s.CustomModelPath;
        _audioService.SetLatencyMode(_selectedLatencyMode);

        _audioService.SoundboardMixer.SoundboardVolumeDb = _soundboardVolumeDb;
        _audioService.SoundboardMixer.MicrophoneVolumeDb = _micVolumeDb;
        _audioService.SoundboardMixer.SendClipsToVirtualMic = _sendClipsToVirtualMic;
        _audioService.SoundboardMixer.MonitorClipsInHeadphones = _monitorClipsInHeadphones;

        // Initialize Soundboard
        RefreshSoundPads();

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

    // =========================================================
    // SOUNDBOARD LOGIC
    // =========================================================

    public void RefreshSoundPads()
    {
        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            SoundPads.Clear();
            var clips = _audioService.SoundboardMixer.Clips;

            int index = 1;
            foreach (var clip in clips)
            {
                var pad = new SoundPadViewModel
                {
                    Id = clip.Id,
                    Name = clip.Name,
                    Shortcut = string.IsNullOrEmpty(clip.ShortcutKey) ? $"{index}" : clip.ShortcutKey,
                    Duration = clip.DurationText,
                    Category = clip.Category,
                    IsEmpty = false,
                    IsPlaying = _audioService.SoundboardMixer.IsPlaying && _audioService.SoundboardMixer.CurrentPlayingClipName == clip.Name
                };

                string clipId = clip.Id;
                pad.PlayCommand = new RelayCommand(() => TogglePlaySound(clipId));
                pad.DeleteCommand = new RelayCommand(() => DeleteSound(clipId));

                SoundPads.Add(pad);
                index++;
            }

            // Fill empty pads so the grid maintains at least 12 pads (3 rows of 4 pads)
            int totalPads = Math.Max(12, ((clips.Count + 3) / 4) * 4);
            int emptyCount = Math.Max(4, totalPads - clips.Count);

            for (int i = 0; i < emptyCount; i++)
            {
                var emptyPad = new SoundPadViewModel
                {
                    Id = $"empty_{i}",
                    Name = "Empty pad",
                    Duration = "Add sound",
                    IsEmpty = true,
                    PlayCommand = new RelayCommand(AddCustomSound)
                };
                SoundPads.Add(emptyPad);
            }

            OnPropertyChanged(nameof(SoundClipsCountText));
            ApplySoundFilter();
        });
    }

    private void ApplySoundFilter()
    {
        FilteredSoundPads.Clear();
        foreach (var pad in SoundPads)
        {
            if (pad.IsEmpty)
            {
                FilteredSoundPads.Add(pad);
                continue;
            }

            bool matchesCategory = SelectedCategory == "All sounds" ||
                string.Equals(pad.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase);

            bool matchesSearch = string.IsNullOrWhiteSpace(SearchSoundText) ||
                pad.Name.Contains(SearchSoundText, StringComparison.OrdinalIgnoreCase);

            if (matchesCategory && matchesSearch)
            {
                FilteredSoundPads.Add(pad);
            }
        }
    }

    private void TogglePlaySound(string clipId)
    {
        if (_audioService.SoundboardMixer.IsPlaying)
        {
            _audioService.SoundboardMixer.StopAll();
        }
        else
        {
            _audioService.SoundboardMixer.PlayClip(clipId);
        }
    }

    public void StopAllSounds()
    {
        _audioService.SoundboardMixer.StopAll();
    }

    public void AddCustomSound()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose Sound Effect for Soundboard",
                Filter = "Audio Files (*.mp3;*.wav;*.ogg;*.flac)|*.mp3;*.wav;*.ogg;*.flac|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                string rawName = Path.GetFileNameWithoutExtension(dialog.FileName);
                // Clean name
                string displayName = char.ToUpper(rawName[0]) + rawName.Substring(1).Replace('_', ' ').Replace('-', ' ');

                int nextKey = _audioService.SoundboardMixer.Clips.Count + 1;
                string shortcut = nextKey <= 9 ? nextKey.ToString() : "";

                _audioService.SoundboardMixer.AddClip(displayName, dialog.FileName, shortcut, "Custom");
                RefreshSoundPads();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to add custom sound: {ex.Message}");
        }
    }

    public void DeleteSound(string clipId)
    {
        _audioService.SoundboardMixer.DeleteClip(clipId);
        RefreshSoundPads();
    }

    public void TriggerShortcut(string key)
    {
        if (key == "Space" || key == "0" || key == "NumPad0")
        {
            StopAllSounds();
            return;
        }

        // Find pad with this shortcut key
        var pad = SoundPads.FirstOrDefault(p => !p.IsEmpty && (p.Shortcut == key || $"NumPad{p.Shortcut}" == key || $"D{p.Shortcut}" == key));
        if (pad != null)
        {
            TogglePlaySound(pad.Id);
        }
    }

    private void OnSoundboardStateChanged()
    {
        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            bool isPlaying = _audioService.SoundboardMixer.IsPlaying;
            string clipName = _audioService.SoundboardMixer.CurrentPlayingClipName;

            NowPlayingText = isPlaying ? $"Playing: {clipName}" : "Nothing playing";

            foreach (var pad in SoundPads)
            {
                if (!pad.IsEmpty)
                {
                    pad.IsPlaying = isPlaying && pad.Name == clipName;
                }
            }
        });
    }

    public void Dispose()
    {
        _audioService.SoundboardMixer.PlaybackStateChanged -= OnSoundboardStateChanged;
        _meterTimer.Stop();
        _meterTimer.Tick -= OnMeterTick;
        _audioService.DisposeAsync().GetAwaiter().GetResult();
    }
}
