using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vixxo.Core.Services.Update;

public class GitHubUpdateChecker
{
    private readonly HttpClient _httpClient;
    private readonly string _repoOwner;
    private readonly string _repoName;

    public GitHubUpdateChecker(HttpClient? httpClient = null, string repoOwner = "bombzyy", string repoName = "vixxo")
    {
        _httpClient = httpClient ?? CreateDefaultClient();
        _repoOwner = repoOwner;
        _repoName = repoName;
    }

    public static HttpClient CreateDefaultClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Vixxo-Desktop-App", "2.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    public static Version ParseSemanticVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new Version(0, 0, 0, 0);
        }

        string cleaned = raw.Trim();
        if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[1..];
        }

        // Strip pre-release tags like -beta, -alpha.1
        int dashIdx = cleaned.IndexOf('-');
        if (dashIdx >= 0)
        {
            cleaned = cleaned[..dashIdx];
        }

        var parts = cleaned.Split('.');
        int major = parts.Length > 0 && int.TryParse(parts[0], out int maj) ? maj : 0;
        int minor = parts.Length > 1 && int.TryParse(parts[1], out int min) ? min : 0;
        int build = parts.Length > 2 && int.TryParse(parts[2], out int bld) ? bld : 0;
        int revision = parts.Length > 3 && int.TryParse(parts[3], out int rev) ? rev : 0;

        return new Version(major, minor, build, revision);
    }

    public static bool IsNewerVersion(string currentVersionStr, string remoteTagStr)
    {
        var current = ParseSemanticVersion(currentVersionStr);
        var remote = ParseSemanticVersion(remoteTagStr);
        return remote > current;
    }

    public UpdateReleaseInfo? ParseReleaseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tProp) ? tProp.GetString() ?? "" : "";
            string title = root.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
            string body = root.TryGetProperty("body", out var bProp) ? bProp.GetString() ?? "" : "";
            bool isPrerelease = root.TryGetProperty("prerelease", out var pProp) && pProp.GetBoolean();
            DateTimeOffset publishedAt = root.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTimeOffset(out var dto)
                ? dto
                : DateTimeOffset.UtcNow;

            string downloadUrl = "";
            string assetName = "";
            long assetSize = 0;

            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                // Prefer Vixxo-Setup-x64.exe or any .exe
                JsonElement? chosenAsset = null;
                foreach (var asset in assets.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                    if (name.Equals("Vixxo-Setup-x64.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        chosenAsset = asset;
                        break;
                    }
                    if (chosenAsset == null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        chosenAsset = asset;
                    }
                }

                if (chosenAsset.HasValue)
                {
                    assetName = chosenAsset.Value.TryGetProperty("name", out var anProp) ? anProp.GetString() ?? "" : "";
                    downloadUrl = chosenAsset.Value.TryGetProperty("browser_download_url", out var dlProp) ? dlProp.GetString() ?? "" : "";
                    assetSize = chosenAsset.Value.TryGetProperty("size", out var sProp) ? sProp.GetInt64() : 0;
                }
            }

            string verString = tagName.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tagName[1..] : tagName;

            return new UpdateReleaseInfo
            {
                TagName = tagName,
                VersionString = verString,
                Title = string.IsNullOrWhiteSpace(title) ? tagName : title,
                Changelog = body,
                DownloadUrl = downloadUrl,
                AssetName = assetName,
                AssetSizeBytes = assetSize,
                PublishedAt = publishedAt,
                IsPrerelease = isPrerelease
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(string currentVersion, CancellationToken ct = default)
    {
        string url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failed(currentVersion, $"GitHub API returned {resp.StatusCode}");
            }

            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var releaseInfo = ParseReleaseJson(json);

            if (releaseInfo == null)
            {
                return UpdateCheckResult.Failed(currentVersion, "Failed to parse release metadata.");
            }

            bool newer = IsNewerVersion(currentVersion, releaseInfo.TagName);
            if (newer)
            {
                return UpdateCheckResult.UpdateAvailable(currentVersion, releaseInfo.VersionString, releaseInfo);
            }
            else
            {
                return UpdateCheckResult.UpToDate(currentVersion, releaseInfo.VersionString, releaseInfo);
            }
        }
        catch (OperationCanceledException)
        {
            return UpdateCheckResult.Failed(currentVersion, "Update check cancelled.");
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Failed(currentVersion, $"Network error: {ex.Message}");
        }
    }

    public async Task DownloadAssetAsync(
        string downloadUrl,
        Stream destinationStream,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
            throw new ArgumentException("Download URL cannot be empty.", nameof(downloadUrl));

        using var req = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        long totalBytes = resp.Content.Headers.ContentLength ?? -1L;
        using var sourceStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        byte[] buffer = new byte[65536]; // 64 KB buffer
        long totalRead = 0;
        int read;

        while ((read = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            await destinationStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            totalRead += read;

            double pct = totalBytes > 0 ? (double)totalRead / totalBytes * 100.0 : 0.0;
            progress?.Report(new UpdateDownloadProgress(totalRead, totalBytes, pct, false));
        }

        progress?.Report(new UpdateDownloadProgress(totalRead, totalBytes, 100.0, true));
    }
}
