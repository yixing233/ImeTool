using System.Net;
using System.Net.Http;
using ImeTool.Updates;

namespace ImeTool.Tests.Updates;

public sealed class GitHubUpdateServiceTests
{
    private const string ReleaseJson = """
        {
          "tag_name": "v1.2.3",
          "html_url": "https://github.com/yixing233/ImeTool/releases/tag/v1.2.3",
          "body": "Release notes",
          "assets": [
            {
              "name": "ImeTool-win-x64.exe",
              "browser_download_url": "https://example.test/ImeTool-win-x64.exe"
            },
            {
              "name": "ImeTool-win-x64.exe.sha256",
              "browser_download_url": "https://example.test/ImeTool-win-x64.exe.sha256"
            }
          ]
        }
        """;

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    public void TryParseVersionTag_HandlesGitHubTags(string tag, int major, int minor, int build)
    {
        bool parsed = AppVersion.TryParseTag(tag, out Version version);

        Assert.True(parsed);
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Theory]
    [InlineData("v2.0.0-beta.1")]
    [InlineData("v1.2.3.4")]
    [InlineData("1.2.3")]
    [InlineData("vv1.2.3")]
    [InlineData("vNext")]
    public void TryParseVersionTag_RejectsUnsupportedTags(string tag)
    {
        Assert.False(AppVersion.TryParseTag(tag, out _));
    }

    [Fact]
    public void ParseRelease_SelectsExecutableAndChecksumAssets()
    {
        UpdateRelease release = GitHubUpdateService.ParseRelease(ReleaseJson);

        Assert.Equal(new Version(1, 2, 3), release.Version);
        Assert.Equal("v1.2.3", release.TagName);
        Assert.Equal("https://example.test/ImeTool-win-x64.exe", release.DownloadUri.AbsoluteUri);
        Assert.Equal("https://example.test/ImeTool-win-x64.exe.sha256", release.ChecksumUri.AbsoluteUri);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsAvailableForNewerRelease()
    {
        using var client = new HttpClient(new StubHandler(HttpStatusCode.OK, ReleaseJson));
        using var service = new GitHubUpdateService(client);

        UpdateCheckResult result = await service.CheckForUpdatesAsync(new Version(1, 0, 0));

        Assert.Equal(UpdateAvailability.Available, result.Availability);
        Assert.Equal(new Version(1, 2, 3), result.Release?.Version);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_HandlesRepositoryWithoutRelease()
    {
        using var client = new HttpClient(new StubHandler(HttpStatusCode.NotFound, "{}"));
        using var service = new GitHubUpdateService(client);

        UpdateCheckResult result = await service.CheckForUpdatesAsync(new Version(1, 0, 0));

        Assert.Equal(UpdateAvailability.NoPublishedRelease, result.Availability);
        Assert.Null(result.Release);
    }

    [Fact]
    public void ParseChecksum_AcceptsStandardSha256File()
    {
        string hash = GitHubUpdateService.ParseChecksum(
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef *ImeTool-win-x64.exe");

        Assert.Equal("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF", hash);
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content)
        });
    }
}
