using System.Net;
using System.Net.Http;
using System.IO.Compression;
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
              "name": "ImeTool_Windows_x64.zip",
              "browser_download_url": "https://example.test/ImeTool_Windows_x64.zip",
              "digest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
            }
          ]
        }
        """;

    [Fact]
    public void DefaultBuildTargetsWindowsX64Package()
    {
        Assert.Equal(AppPackage.WindowsX64AssetName, AppPackage.UpdateAssetName);
    }

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    public void TryParseVersionTag_HandlesGitHubTags(string tag, int major, int minor, int build)
    {
        bool parsed = AppVersion.TryParseTag(tag, out Version version);

        Assert.True(parsed);
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Fact]
    public void ParseRelease_SelectsWindowsPackageAndGitHubDigest()
    {
        UpdateRelease release = GitHubUpdateService.ParseRelease(ReleaseJson);

        Assert.EndsWith("ImeTool_Windows_x64.zip", release.DownloadUri.AbsoluteUri);
        Assert.Equal(
            "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
            release.Sha256);
    }

    [Fact]
    public void ExtractUpdatePayload_ExtractsOnlyExpectedExecutableFromZip()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string archivePath = Path.Combine(directory, AppPackage.WindowsX64AssetName);
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            ZipArchiveEntry executable = archive.CreateEntry("ImeTool.exe");
            using (Stream stream = executable.Open())
            {
                stream.Write([0x4D, 0x5A, 0x01, 0x02]);
            }

            ZipArchiveEntry ignored = archive.CreateEntry("ignored.txt");
            using (Stream ignoredStream = ignored.Open())
            {
                ignoredStream.Write([0x01]);
            }
        }

        string extractedPath = GitHubUpdateService.ExtractUpdatePayload(archivePath);

        Assert.True(File.Exists(extractedPath));
        Assert.Equal([0x4D, 0x5A, 0x01, 0x02], File.ReadAllBytes(extractedPath));
        Assert.False(File.Exists(archivePath));
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
    public void ParseDigest_AcceptsGitHubSha256Digest()
    {
        string hash = GitHubUpdateService.ParseDigest(
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");

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
