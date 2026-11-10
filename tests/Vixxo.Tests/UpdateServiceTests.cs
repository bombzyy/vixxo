using Vixxo.Core.Services.Update;
using Xunit;

namespace Vixxo.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("v2.0.0", 2, 0, 0, 0)]
    [InlineData("2.1.3", 2, 1, 3, 0)]
    [InlineData("v1.9.4.5", 1, 9, 4, 5)]
    [InlineData("v2.0.1-beta.2", 2, 0, 1, 0)]
    [InlineData("V3.0.0-rc1", 3, 0, 0, 0)]
    [InlineData("", 0, 0, 0, 0)]
    [InlineData(null, 0, 0, 0, 0)]
    [InlineData("invalid", 0, 0, 0, 0)]
    public void ParseSemanticVersion_ValidStrings_ParsesCorrectly(string? input, int major, int minor, int build, int rev)
    {
        var ver = GitHubUpdateChecker.ParseSemanticVersion(input);
        Assert.Equal(major, ver.Major);
        Assert.Equal(minor, ver.Minor);
        Assert.Equal(build, ver.Build);
        Assert.Equal(rev, ver.Revision);
    }

    [Theory]
    [InlineData("2.0.0", "v2.0.1", true)]
    [InlineData("2.0.0", "v2.1.0", true)]
    [InlineData("2.0.0", "v3.0.0", true)]
    [InlineData("2.0.0", "v2.0.0", false)]
    [InlineData("2.1.0", "v2.0.9", false)]
    [InlineData("2.0.0", "v2.0.0-beta", false)]
    [InlineData("1.0.0", "2.0.0", true)]
    public void IsNewerVersion_ComparesCorrectly(string current, string remote, bool expectedNewer)
    {
        bool result = GitHubUpdateChecker.IsNewerVersion(current, remote);
        Assert.Equal(expectedNewer, result);
    }

    [Fact]
    public void ParseReleaseJson_ExtractsAssetAndMetadata_Successfully()
    {
        var checker = new GitHubUpdateChecker();
        string sampleJson = """
        {
          "tag_name": "v2.1.0",
          "name": "Vixxo 2.1.0 — AI Voice Engine & Performance Boost",
          "body": "## What's New\n- Real-time RVC v2 Voice Engine\n- Low latency audio pipeline",
          "prerelease": false,
          "published_at": "2026-11-10T12:00:00Z",
          "assets": [
            {
              "name": "checksums.txt",
              "size": 256,
              "browser_download_url": "https://github.com/bombzyy/vixxo/releases/download/v2.1.0/checksums.txt"
            },
            {
              "name": "Vixxo-Setup-x64.exe",
              "size": 524288000,
              "browser_download_url": "https://github.com/bombzyy/vixxo/releases/download/v2.1.0/Vixxo-Setup-x64.exe"
            }
          ]
        }
        """;

        var info = checker.ParseReleaseJson(sampleJson);

        Assert.NotNull(info);
        Assert.Equal("v2.1.0", info.TagName);
        Assert.Equal("2.1.0", info.VersionString);
        Assert.Equal("Vixxo 2.1.0 — AI Voice Engine & Performance Boost", info.Title);
        Assert.Contains("Real-time RVC v2 Voice Engine", info.Changelog);
        Assert.Equal("Vixxo-Setup-x64.exe", info.AssetName);
        Assert.Equal("https://github.com/bombzyy/vixxo/releases/download/v2.1.0/Vixxo-Setup-x64.exe", info.DownloadUrl);
        Assert.Equal(524288000, info.AssetSizeBytes);
        Assert.False(info.IsPrerelease);
    }

    [Fact]
    public void ParseReleaseJson_MalformedJson_ReturnsNull()
    {
        var checker = new GitHubUpdateChecker();
        var info = checker.ParseReleaseJson("not json");
        Assert.Null(info);
    }
}
