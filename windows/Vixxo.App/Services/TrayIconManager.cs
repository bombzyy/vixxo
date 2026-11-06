using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Vixxo.App.ViewModels;
using Application = System.Windows.Application;

namespace Vixxo.App.Services;

public sealed class TrayIconManager : IDisposable
{
    private readonly Window _mainWindow;
    private readonly MainViewModel _viewModel;
    private readonly SettingsService _settingsService;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _muteMenuItem;
    private readonly ToolStripMenuItem _disconnectMenuItem;
    private bool _isDisposed;

    public TrayIconManager(Window mainWindow, MainViewModel viewModel, SettingsService settingsService)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

        _notifyIcon = new NotifyIcon();

        // Load Icon
        try
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(iconPath))
            {
                _notifyIcon.Icon = new Icon(iconPath);
            }
            else
            {
                _notifyIcon.Icon = SystemIcons.Application;
            }
        }
        catch
        {
            _notifyIcon.Icon = SystemIcons.Application;
        }

        _notifyIcon.Text = "Vixxo Microphone Server";
        _notifyIcon.Visible = true;

        // Context Menu
        var contextMenu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("Open Vixxo", null, (s, e) => RestoreWindow());
        openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);

        _muteMenuItem = new ToolStripMenuItem("Mute Microphone", null, (s, e) => _viewModel.IsMuted = !_viewModel.IsMuted);
        _disconnectMenuItem = new ToolStripMenuItem("Disconnect Phone", null, (s, e) => _viewModel.DisconnectClientCommand.Execute(null));

        var exitItem = new ToolStripMenuItem("Exit", null, (s, e) =>
        {
            _notifyIcon.Visible = false;
            if (_mainWindow is MainWindow mw)
            {
                mw.ShutdownApplication();
            }
            else
            {
                Application.Current.Shutdown();
            }
        });

        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_muteMenuItem);
        contextMenu.Items.Add(_disconnectMenuItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;

        _notifyIcon.DoubleClick += (s, e) => RestoreWindow();
        _notifyIcon.MouseClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                RestoreWindow();
            }
        };

        // Hook window state
        _mainWindow.StateChanged += OnWindowStateChanged;

        // Track ViewModel changes for tray update
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsMuted))
            {
                UpdateTrayState();
            }
            else if (e.PropertyName == nameof(MainViewModel.ServerState) || e.PropertyName == nameof(MainViewModel.ConnectedClient))
            {
                UpdateTrayState();
            }
        };

        UpdateTrayState();
    }

    public void RestoreWindow()
    {
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }
        _mainWindow.Activate();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_mainWindow.WindowState == WindowState.Minimized && _settingsService.CurrentSettings.MinimizeToTray)
        {
            _mainWindow.Hide();
            _notifyIcon.ShowBalloonTip(1500, "Vixxo", "Vixxo is running in the background.", ToolTipIcon.Info);
        }
    }

    private void UpdateTrayState()
    {
        _muteMenuItem.Text = _viewModel.IsMuted ? "Unmute Microphone" : "Mute Microphone";
        _disconnectMenuItem.Enabled = _viewModel.ConnectedClient != "None";

        string tip = $"Vixxo: {_viewModel.StatusText}";
        if (_viewModel.ConnectedClient != "None")
        {
            tip += $" ({_viewModel.ConnectedClient})";
        }
        if (tip.Length > 63) tip = tip.Substring(0, 63);
        _notifyIcon.Text = tip;
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(3000, title, text, icon);
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _mainWindow.StateChanged -= OnWindowStateChanged;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}
