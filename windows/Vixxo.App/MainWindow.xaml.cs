using System.ComponentModel;
using System.Windows;
using Vixxo.App.Services;
using Vixxo.App.ViewModels;

namespace Vixxo.App;

public partial class MainWindow : Window
{
    private readonly AudioService _audioService;
    private readonly SettingsService _settingsService;
    private readonly MainViewModel _viewModel;
    private readonly TrayIconManager _trayIconManager;
    private bool _isExplicitShutdown;

    public MainWindow()
    {
        InitializeComponent();

        _settingsService = new SettingsService();
        _audioService = new AudioService();
        _viewModel = new MainViewModel(_audioService, _settingsService);

        DataContext = _viewModel;

        _trayIconManager = new TrayIconManager(this, _viewModel, _settingsService);
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Suspend)
        {
            _audioService.StopServer();
        }
        else if (e.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _viewModel.RefreshDevices();
                _audioService.StartServer(_settingsService.CurrentSettings.Port);
            });
        }
    }

    public void ShutdownApplication()
    {
        _isExplicitShutdown = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExplicitShutdown && _settingsService.CurrentSettings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            _trayIconManager.ShowBalloon("Vixxo", "Vixxo is still running in the background. Use the tray icon to open or exit.");
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _trayIconManager.Dispose();
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
