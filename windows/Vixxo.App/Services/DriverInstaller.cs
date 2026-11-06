using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace Vixxo.App.Services;

public static class DriverInstaller
{
    private const string DriverDownloadUrl = "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack46.zip";

    public static async Task<bool> InstallVbCableAsync(IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report("Locating driver files...");
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localDriverDir = Path.Combine(baseDir, "Assets", "driver");
            string setupExe = Path.Combine(localDriverDir, "VBCABLE_Setup_x64.exe");

            // If not bundled locally, check temp or download
            if (!File.Exists(setupExe))
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "Vixxo_VbCable");
                Directory.CreateDirectory(tempDir);
                setupExe = Path.Combine(tempDir, "VBCABLE_Setup_x64.exe");

                if (!File.Exists(setupExe))
                {
                    progress?.Report("Downloading VB-CABLE driver package...");
                    using (var client = new HttpClient())
                    {
                        var data = await client.GetByteArrayAsync(DriverDownloadUrl).ConfigureAwait(false);
                        string zipFile = Path.Combine(tempDir, "vbcable.zip");
                        await File.WriteAllBytesAsync(zipFile, data).ConfigureAwait(false);
                        progress?.Report("Extracting driver files...");
                        ZipFile.ExtractToDirectory(zipFile, tempDir, true);
                        try { File.Delete(zipFile); } catch { }
                    }
                }
            }

            if (!File.Exists(setupExe))
            {
                progress?.Report("Driver executable not found.");
                return false;
            }

            progress?.Report("Installing virtual driver (Administrator approval required)...");
            var psi = new ProcessStartInfo
            {
                FileName = setupExe,
                Arguments = "-i -h",
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
                progress?.Report("Driver installed! Initializing audio endpoints...");
                await Task.Delay(2500).ConfigureAwait(false); // Wait for Windows PnP audio endpoint registration
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            progress?.Report($"Driver installation canceled or failed: {ex.Message}");
            return false;
        }
    }
}
