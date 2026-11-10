using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using Vixxo.Core.Services.Update;

namespace Vixxo.App.Services;

public sealed class AppUpdateService
{
    private static readonly Lazy<AppUpdateService> _instance = new(() => new AppUpdateService());
    public static AppUpdateService Instance => _instance.Value;

    public GitHubUpdateChecker Checker { get; }
    public string CurrentVersion { get; }

    public AppUpdateService(GitHubUpdateChecker? checker = null)
    {
        Checker = checker ?? new GitHubUpdateChecker(repoOwner: "bombzyy", repoName: "vixxo");
        CurrentVersion = GetAppVersion();
    }

    private static string GetAppVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var ver = asm.GetName().Version;
        if (ver != null && (ver.Major > 0 || ver.Minor > 0 || ver.Build > 0))
        {
            return $"{ver.Major}.{ver.Minor}.{ver.Build}";
        }
        return "2.0.0";
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        return await Checker.CheckForUpdateAsync(CurrentVersion, ct).ConfigureAwait(false);
    }

    public async Task<string> DownloadUpdateAsync(
        UpdateReleaseInfo release,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(release.DownloadUrl))
        {
            throw new InvalidOperationException("Release does not contain an executable installer download URL.");
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "VixxoUpdate");
        Directory.CreateDirectory(tempDir);

        string installerFileName = string.IsNullOrWhiteSpace(release.AssetName) ? "Vixxo-Setup-x64.exe" : release.AssetName;
        string destinationPath = Path.Combine(tempDir, installerFileName);

        // Delete any existing partial download
        if (File.Exists(destinationPath))
        {
            try { File.Delete(destinationPath); } catch { /* ignore */ }
        }

        using (var fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
        {
            await Checker.DownloadAssetAsync(release.DownloadUrl, fs, progress, ct).ConfigureAwait(false);
        }

        return destinationPath;
    }

    public void LaunchInstallerAndRestart(string installerPath)
    {
        if (!File.Exists(installerPath))
        {
            throw new FileNotFoundException("Installer binary not found at destination path.", installerPath);
        }

        string currentExe = Environment.ProcessPath 
            ?? Process.GetCurrentProcess().MainModule?.FileName 
            ?? @"C:\Users\somya\AppData\Local\Programs\Vixxo\Vixxo.exe";

        // Spawns a detached shell command:
        // 1. Wait 1 second for Vixxo.exe to fully shut down.
        // 2. Run installer in silent mode with /CLOSEAPPLICATIONS.
        // 3. Once installation completes successfully, re-launch updated Vixxo.exe.
        string cmdArgs = $"/c \"timeout /t 1 /nobreak >nul && start /wait \"\" \"{installerPath}\" /SILENT /CLOSEAPPLICATIONS && start \"\" \"{currentExe}\"\"";

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = cmdArgs,
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process.Start(psi);

        // Terminate current process gracefully
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            System.Windows.Application.Current.Shutdown();
        });
    }
}
