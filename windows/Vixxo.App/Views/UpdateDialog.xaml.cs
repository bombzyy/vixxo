using System.Windows;
using Vixxo.App.Services;
using Vixxo.Core.Services.Update;

namespace Vixxo.App.Views;

public partial class UpdateDialog : Window
{
    private readonly UpdateReleaseInfo _release;
    private CancellationTokenSource? _downloadCts;

    public UpdateDialog(UpdateReleaseInfo release, string currentVersion)
    {
        InitializeComponent();
        _release = release;

        ReleaseTitleTextBlock.Text = string.IsNullOrWhiteSpace(release.Title) ? release.TagName : release.Title;
        CurrentVersionTextBlock.Text = $"Current: v{currentVersion}";
        LatestVersionTextBlock.Text = $"Latest: {(release.TagName.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? release.TagName : "v" + release.TagName)}";
        ReleaseDateTextBlock.Text = release.PublishedAt.ToString("MMM dd, yyyy");
        ChangelogTextBlock.Text = string.IsNullOrWhiteSpace(release.Changelog) 
            ? "New performance improvements and stability updates." 
            : release.Changelog;
    }

    private void OnLaterClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _downloadCts?.Cancel();
        ResetUiToReady();
    }

    private void ResetUiToReady()
    {
        ProgressSection.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        LaterButton.Visibility = Visibility.Visible;
        UpdateNowButton.IsEnabled = true;
        UpdateNowButton.Content = "🚀 Update Now";
    }

    private async void OnUpdateNowClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_release.DownloadUrl))
        {
            ErrorMessageTextBlock.Text = "Download link not available for this release.";
            return;
        }

        UpdateNowButton.IsEnabled = false;
        LaterButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Visible;
        ProgressSection.Visibility = Visibility.Visible;
        ErrorMessageTextBlock.Text = "";
        DownloadProgressBar.Value = 0;
        ProgressPercentageTextBlock.Text = "0%";
        ProgressStatusTextBlock.Text = "Connecting to GitHub...";

        _downloadCts = new CancellationTokenSource();

        var progress = new Progress<UpdateDownloadProgress>(p =>
        {
            DownloadProgressBar.Value = p.ProgressPercentage;
            ProgressPercentageTextBlock.Text = $"{p.ProgressPercentage:F0}%";
            if (p.TotalBytes > 0)
            {
                double currentMb = p.BytesDownloaded / 1048576.0;
                double totalMb = p.TotalBytes / 1048576.0;
                ProgressStatusTextBlock.Text = $"Downloading update... {currentMb:F1} MB / {totalMb:F1} MB";
            }
            else
            {
                double currentMb = p.BytesDownloaded / 1048576.0;
                ProgressStatusTextBlock.Text = $"Downloading update... {currentMb:F1} MB";
            }
        });

        try
        {
            string installerPath = await AppUpdateService.Instance.DownloadUpdateAsync(_release, progress, _downloadCts.Token);
            ProgressStatusTextBlock.Text = "Download complete! Installing & restarting...";
            UpdateNowButton.Content = "Installing...";

            // Short UI delay so user sees "Download complete!"
            await Task.Delay(800);

            AppUpdateService.Instance.LaunchInstallerAndRestart(installerPath);
        }
        catch (OperationCanceledException)
        {
            ResetUiToReady();
            ErrorMessageTextBlock.Text = "Download cancelled.";
        }
        catch (Exception ex)
        {
            ResetUiToReady();
            ErrorMessageTextBlock.Text = $"Update failed: {ex.Message}";
        }
    }
}
