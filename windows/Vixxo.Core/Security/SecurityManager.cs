using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Vixxo.Core.Security;

public record TrustedDevice(
    string DeviceId,
    string DeviceName,
    string Credential,
    DateTime PairedAt);

public sealed class SecurityManager
{
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Vixxo");

    private static readonly string CertFilePath = Path.Combine(AppDataFolder, "vixxo_server.pfx");
    private static readonly string TrustFilePath = Path.Combine(AppDataFolder, "trusted_devices.json");
    private const string CertPassword = "vixxo-internal-secure-key";

    private readonly object _lock = new();
    private X509Certificate2? _serverCertificate;
    private readonly List<TrustedDevice> _trustedDevices = new();

    // Active Pairing Token & 6-digit PIN
    private string? _activePairingPin;
    private string? _activePairingToken;
    private DateTime _pairingExpiryUtc;

    // Rate Limiting (Max 5 attempts in 5 minutes per remote endpoint)
    private readonly Dictionary<string, (int attempts, DateTime resetUtc)> _rateLimits = new();

    public SecurityManager()
    {
        LoadOrCreateCertificate();
        LoadTrustedDevices();
    }

    public X509Certificate2 ServerCertificate => _serverCertificate!;
    public string ServerCertificateFingerprint => GetSha256Fingerprint(_serverCertificate!);

    public IReadOnlyList<TrustedDevice> TrustedDevices
    {
        get
        {
            lock (_lock) return _trustedDevices.ToList();
        }
    }

    public (string pin, string token, DateTime expiryUtc) GenerateNewPairingSession()
    {
        lock (_lock)
        {
            // 6-digit numeric PIN
            int pinNumber = RandomNumberGenerator.GetInt32(100000, 999999);
            _activePairingPin = pinNumber.ToString();

            // 16-byte random token
            byte[] tokenBytes = new byte[16];
            RandomNumberGenerator.Fill(tokenBytes);
            _activePairingToken = Convert.ToHexString(tokenBytes).ToLowerInvariant();

            _pairingExpiryUtc = DateTime.UtcNow.AddMinutes(5);

            return (_activePairingPin, _activePairingToken, _pairingExpiryUtc);
        }
    }

    public bool ValidatePairingAttempt(string clientAddress, string? submittedPinOrToken, string deviceId, string deviceName, out string newDeviceCredential)
    {
        newDeviceCredential = string.Empty;

        lock (_lock)
        {
            // Check rate limiting
            DateTime now = DateTime.UtcNow;
            if (_rateLimits.TryGetValue(clientAddress, out var rate))
            {
                if (now < rate.resetUtc)
                {
                    if (rate.attempts >= 5)
                    {
                        return false; // Locked out
                    }
                }
                else
                {
                    _rateLimits[clientAddress] = (0, now.AddMinutes(5));
                }
            }
            else
            {
                _rateLimits[clientAddress] = (0, now.AddMinutes(5));
            }

            // Check if PIN or token matches active session and has not expired
            string cleanSubmitted = (submittedPinOrToken ?? "").Replace(" ", "").Trim();
            string cleanPin = (_activePairingPin ?? "").Replace(" ", "").Trim();
            string cleanToken = (_activePairingToken ?? "").Replace(" ", "").Trim();

            if (string.IsNullOrWhiteSpace(cleanSubmitted) || now > _pairingExpiryUtc)
            {
                RecordFailedAttempt(clientAddress);
                return false;
            }

            bool matches = (!string.IsNullOrEmpty(cleanPin) && string.Equals(cleanSubmitted, cleanPin, StringComparison.OrdinalIgnoreCase)) ||
                           (!string.IsNullOrEmpty(cleanToken) && string.Equals(cleanSubmitted, cleanToken, StringComparison.OrdinalIgnoreCase));

            if (!matches)
            {
                RecordFailedAttempt(clientAddress);
                return false;
            }

            // Pairing successful! Issue persistent device credential
            byte[] credBytes = new byte[32];
            RandomNumberGenerator.Fill(credBytes);
            newDeviceCredential = Convert.ToHexString(credBytes).ToLowerInvariant();

            // Clear used PIN / token (single-use)
            _activePairingPin = null;
            _activePairingToken = null;

            // Save trusted device
            _trustedDevices.RemoveAll(d => d.DeviceId == deviceId);
            _trustedDevices.Add(new TrustedDevice(deviceId, deviceName, newDeviceCredential, DateTime.UtcNow));
            SaveTrustedDevices();

            return true;
        }
    }

    public bool ValidateDeviceCredential(string deviceId, string credential)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(credential)) return false;

            var device = _trustedDevices.FirstOrDefault(d => d.DeviceId == deviceId);
            if (device == null) return false;

            // Constant-time comparison
            byte[] a = System.Text.Encoding.UTF8.GetBytes(device.Credential);
            byte[] b = System.Text.Encoding.UTF8.GetBytes(credential);
            return CryptographicOperations.FixedTimeEquals(a, b);
        }
    }

    public bool RevokeDevice(string deviceId)
    {
        lock (_lock)
        {
            int removed = _trustedDevices.RemoveAll(d => d.DeviceId == deviceId);
            if (removed > 0)
            {
                SaveTrustedDevices();
                return true;
            }
            return false;
        }
    }

    private void RecordFailedAttempt(string clientAddress)
    {
        if (_rateLimits.TryGetValue(clientAddress, out var rate))
        {
            _rateLimits[clientAddress] = (rate.attempts + 1, rate.resetUtc);
        }
    }

    private void LoadOrCreateCertificate()
    {
        if (!Directory.Exists(AppDataFolder))
        {
            Directory.CreateDirectory(AppDataFolder);
        }

        if (File.Exists(CertFilePath))
        {
            try
            {
                _serverCertificate = new X509Certificate2(CertFilePath, CertPassword, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
                if (_serverCertificate.NotAfter > DateTime.UtcNow.AddDays(7))
                {
                    return;
                }
            }
            catch
            {
                // Re-create if corrupt
            }
        }

        // Generate Self-Signed Certificate
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Vixxo Virtual Microphone Server", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));

        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        byte[] pfxBytes = cert.Export(X509ContentType.Pfx, CertPassword);
        File.WriteAllBytes(CertFilePath, pfxBytes);

        _serverCertificate = new X509Certificate2(pfxBytes, CertPassword, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
    }

    private void LoadTrustedDevices()
    {
        try
        {
            if (File.Exists(TrustFilePath))
            {
                string json = File.ReadAllText(TrustFilePath);
                var loaded = JsonSerializer.Deserialize<List<TrustedDevice>>(json);
                if (loaded != null)
                {
                    _trustedDevices.Clear();
                    _trustedDevices.AddRange(loaded);
                }
            }
        }
        catch { }
    }

    private void SaveTrustedDevices()
    {
        try
        {
            string json = JsonSerializer.Serialize(_trustedDevices, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(TrustFilePath, json);
        }
        catch { }
    }

    public static string GetSha256Fingerprint(X509Certificate2 cert)
    {
        byte[] hash = cert.GetCertHash(HashAlgorithmName.SHA256);
        return BitConverter.ToString(hash).Replace("-", ":");
    }
}
