namespace Vixxo.Core.Services.Update;

public sealed class UpdateReleaseInfo
{
    public string TagName { get; init; } = string.Empty;
    public string VersionString { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Changelog { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string AssetName { get; init; } = string.Empty;
    public long AssetSizeBytes { get; init; }
    public DateTimeOffset PublishedAt { get; init; }
    public bool IsPrerelease { get; init; }
}

public sealed class UpdateCheckResult
{
    public bool HasUpdate { get; init; }
    public string CurrentVersion { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public UpdateReleaseInfo? ReleaseInfo { get; init; }
    public string? ErrorMessage { get; init; }

    public static UpdateCheckResult UpToDate(string currentVersion, string latestVersion, UpdateReleaseInfo info) =>
        new()
        {
            HasUpdate = false,
            CurrentVersion = currentVersion,
            LatestVersion = latestVersion,
            ReleaseInfo = info
        };

    public static UpdateCheckResult UpdateAvailable(string currentVersion, string latestVersion, UpdateReleaseInfo info) =>
        new()
        {
            HasUpdate = true,
            CurrentVersion = currentVersion,
            LatestVersion = latestVersion,
            ReleaseInfo = info
        };

    public static UpdateCheckResult Failed(string currentVersion, string error) =>
        new()
        {
            HasUpdate = false,
            CurrentVersion = currentVersion,
            ErrorMessage = error
        };
}

public readonly record struct UpdateDownloadProgress(long BytesDownloaded, long TotalBytes, double ProgressPercentage, bool IsComplete);
