using Vixxo.Core.Security;

namespace Vixxo.Tests;

public class SecurityManagerTests
{
    [Fact]
    public void SecurityManager_GeneratesValidCertificateAndFingerprint()
    {
        var manager = new SecurityManager();
        var cert = manager.ServerCertificate;
        Assert.NotNull(cert);
        Assert.True(cert.HasPrivateKey);

        string fp = manager.ServerCertificateFingerprint;
        Assert.False(string.IsNullOrWhiteSpace(fp));
        Assert.Contains(":", fp);
    }

    [Fact]
    public void PairingSession_Generates6DigitPinAndValidates()
    {
        var manager = new SecurityManager();
        var (pin, token, expiry) = manager.GenerateNewPairingSession();

        Assert.Equal(6, pin.Length);
        Assert.True(int.TryParse(pin, out _));
        Assert.True(expiry > DateTime.UtcNow);

        // Valid PIN pairing
        bool success = manager.ValidatePairingAttempt("127.0.0.1", pin, "device-1", "TestPhone", out string cred);
        Assert.True(success);
        Assert.False(string.IsNullOrWhiteSpace(cred));

        // PIN should be consumed (one-time use)
        bool reuse = manager.ValidatePairingAttempt("127.0.0.1", pin, "device-2", "OtherPhone", out _);
        Assert.False(reuse);

        // Device should now be trusted
        Assert.True(manager.ValidateDeviceCredential("device-1", cred));

        // Revocation
        bool revoked = manager.RevokeDevice("device-1");
        Assert.True(revoked);
        Assert.False(manager.ValidateDeviceCredential("device-1", cred));
    }

    [Fact]
    public void RateLimiting_BlocksAfterFiveFailedAttempts()
    {
        var manager = new SecurityManager();
        manager.GenerateNewPairingSession();

        string clientIp = "192.168.1.99";

        // 5 bad attempts
        for (int i = 0; i < 5; i++)
        {
            manager.ValidatePairingAttempt(clientIp, "000000", "device-x", "Hacker", out _);
        }

        // 6th attempt should be blocked even if PIN is right
        var (pin, _, _) = manager.GenerateNewPairingSession();
        bool blocked = manager.ValidatePairingAttempt(clientIp, pin, "device-x", "Hacker", out _);
        Assert.False(blocked);
    }
}
