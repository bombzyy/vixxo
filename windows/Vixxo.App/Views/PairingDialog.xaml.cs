using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QRCoder;
using Vixxo.Core.Security;

namespace Vixxo.App.Views;

public partial class PairingDialog : Window
{
    private readonly SecurityManager _securityManager;
    private readonly string _primaryIp;
    private readonly int _port;
    private readonly DispatcherTimer _countdownTimer;
    private DateTime _expiryUtc;

    public PairingDialog(SecurityManager securityManager, string primaryIp, int port)
    {
        InitializeComponent();

        _securityManager = securityManager ?? throw new ArgumentNullException(nameof(securityManager));
        _primaryIp = primaryIp;
        _port = port;

        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += OnCountdownTick;

        GeneratePairingQr();
        RefreshTrustedDevicesList();
    }

    private void GeneratePairingQr()
    {
        var (pin, token, expiry) = _securityManager.GenerateNewPairingSession();
        _expiryUtc = expiry;

        // Display PIN: e.g. "123 456"
        PinTextBlock.Text = $"{pin.Substring(0, 3)} {pin.Substring(3, 3)}";
        ServerIpTextBlock.Text = $"Server IP: {_primaryIp} : {_port}";

        // Create QR Payload
        var qrData = new
        {
            v = 1,
            name = Environment.MachineName,
            host = _primaryIp,
            port = _port,
            pin = pin,
            token = token,
            fp = _securityManager.ServerCertificateFingerprint
        };

        string jsonPayload = JsonSerializer.Serialize(qrData);

        // Generate QR Code PNG
        using var generator = new QRCodeGenerator();
        using var qrCodeData = generator.CreateQrCode(jsonPayload, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(qrCodeData);
        byte[] qrBytes = qrCode.GetGraphic(10);

        using var ms = new MemoryStream(qrBytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = ms;
        bitmap.EndInit();
        bitmap.Freeze();

        QrCodeImage.Source = bitmap;

        _countdownTimer.Start();
        UpdateCountdownDisplay();
    }

    private void OnCountdownTick(object? sender, EventArgs e)
    {
        UpdateCountdownDisplay();
    }

    private void UpdateCountdownDisplay()
    {
        var remaining = _expiryUtc - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            TimerTextBlock.Text = "PIN expired. Please generate a new PIN.";
            _countdownTimer.Stop();
        }
        else
        {
            TimerTextBlock.Text = $"PIN expires in {remaining.Minutes:D2}:{remaining.Seconds:D2}";
        }
    }

    private void RefreshTrustedDevicesList()
    {
        TrustedDevicesListView.ItemsSource = null;
        TrustedDevicesListView.ItemsSource = _securityManager.TrustedDevices;
    }

    private void OnRefreshPinClick(object sender, RoutedEventArgs e)
    {
        GeneratePairingQr();
    }

    private void OnRevokeDeviceClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string deviceId)
        {
            _securityManager.RevokeDevice(deviceId);
            RefreshTrustedDevicesList();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        _countdownTimer.Stop();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _countdownTimer.Stop();
        base.OnClosed(e);
    }
}
