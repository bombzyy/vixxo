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

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        // Don't intercept shortcuts when typing in a TextBox
        if (e.OriginalSource is System.Windows.Controls.TextBox)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        if (e.Key == System.Windows.Input.Key.Space)
        {
            _viewModel.TriggerShortcut("Space");
            e.Handled = true;
        }
        else if (e.Key >= System.Windows.Input.Key.D1 && e.Key <= System.Windows.Input.Key.D9)
        {
            _viewModel.TriggerShortcut(((int)e.Key - (int)System.Windows.Input.Key.D0).ToString());
            e.Handled = true;
        }
        else if (e.Key >= System.Windows.Input.Key.NumPad1 && e.Key <= System.Windows.Input.Key.NumPad9)
        {
            _viewModel.TriggerShortcut(((int)e.Key - (int)System.Windows.Input.Key.NumPad0).ToString());
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.NumPad0 || e.Key == System.Windows.Input.Key.D0)
        {
            _viewModel.TriggerShortcut("0");
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
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
