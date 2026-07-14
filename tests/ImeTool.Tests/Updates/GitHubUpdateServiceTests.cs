using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
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
              "name": "ImeTool_Windows_x64.exe",
              "browser_download_url": "https://example.test/ImeTool_Windows_x64.exe",
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

        Assert.EndsWith("ImeTool_Windows_x64.exe", release.DownloadUri.AbsoluteUri);
        Assert.Equal(
            "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
            release.Sha256);
    }

    [Fact]
    public void ValidateInstallerPayload_AcceptsWindowsExecutable()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string installerPath = Path.Combine(directory, AppPackage.WindowsX64AssetName);
        File.WriteAllBytes(installerPath, [0x4D, 0x5A, 0x01, 0x02]);

        string validatedPath = GitHubUpdateService.ValidateInstallerPayload(installerPath);

        Assert.Equal(installerPath, validatedPath);
    }

    [Fact]
    public void ValidateInstallerPayload_RejectsNonExecutablePayload()
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, "not an installer");

        Assert.Throws<InvalidDataException>(() =>
            GitHubUpdateService.ValidateInstallerPayload(path));
    }

    [Fact]
    public async Task DownloadUpdateAsync_SavesValidatedInstallerWithExeExtension()
    {
        byte[] payload = [0x4D, 0x5A, 0x01, 0x02];
        string checksum = Convert.ToHexString(SHA256.HashData(payload));
        string tagName = $"v-test-{Guid.NewGuid():N}";
        using var client = new HttpClient(new RoutingHandler(_ =>
            Response(HttpStatusCode.OK, new ByteArrayContent(payload))));
        using var service = new GitHubUpdateService(client);
        var release = new UpdateRelease(
            new Version(2, 0, 0),
            tagName,
            new Uri("https://example.test/ImeTool_Windows_x64.exe"),
            checksum,
            new Uri("https://example.test/release"),
            string.Empty);

        string installerPath = await service.DownloadUpdateAsync(release);

        Assert.EndsWith(AppPackage.WindowsX64AssetName, installerPath);
        Assert.Equal(payload, File.ReadAllBytes(installerPath));
        Directory.Delete(Path.GetDirectoryName(installerPath)!, recursive: true);
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
    public async Task CheckForUpdatesAsync_UsesReleasePageWithoutCallingRateLimitedApi()
    {
        const string digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        const string unrelatedDigest = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
        var requestedUris = new List<Uri>();
        using var client = new HttpClient(new RoutingHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);
            if (request.RequestUri!.AbsoluteUri == GitHubUpdateService.LatestReleasePage)
            {
                return Response(
                    HttpStatusCode.OK,
                    "<html>latest release</html>",
                    "https://github.com/yixing233/ImeTool/releases/tag/v1.2.3");
            }

            if (request.RequestUri.AbsoluteUri ==
                "https://github.com/yixing233/ImeTool/releases/expanded_assets/v1.2.3")
            {
                return Response(
                    HttpStatusCode.OK,
                    $"<li><a href=\"/yixing233/ImeTool/releases/download/v1.2.3/ImeTool_Windows_x64.exe\">ImeTool_Windows_x64.exe</a><span>sha256:{digest}</span></li>" +
                    $"<li><a href=\"/yixing233/ImeTool/releases/download/v1.2.3/other.zip\">other.zip</a><span>sha256:{unrelatedDigest}</span></li>");
            }

            return Response(HttpStatusCode.Forbidden, "API rate limit exceeded");
        }));
        using var service = new GitHubUpdateService(client);

        UpdateCheckResult result = await service.CheckForUpdatesAsync(new Version(1, 0, 0));

        Assert.Equal(UpdateAvailability.Available, result.Availability);
        Assert.Equal(new Version(1, 2, 3), result.Release?.Version);
        Assert.Equal(
            "https://github.com/yixing233/ImeTool/releases/download/v1.2.3/ImeTool_Windows_x64.exe",
            result.Release?.DownloadUri.AbsoluteUri);
        Assert.DoesNotContain(requestedUris, uri => uri.AbsoluteUri == GitHubUpdateService.LatestReleaseApi);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_DoesNotUseDigestFromAnotherAsset()
    {
        const string unrelatedDigest = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
        bool apiCalled = false;
        using var client = new HttpClient(new RoutingHandler(request =>
        {
            if (request.RequestUri!.AbsoluteUri == GitHubUpdateService.LatestReleasePage)
            {
                return Response(
                    HttpStatusCode.OK,
                    "<html>latest release</html>",
                    "https://github.com/yixing233/ImeTool/releases/tag/v1.2.3");
            }

            if (request.RequestUri.AbsoluteUri == GitHubUpdateService.LatestReleaseApi)
            {
                apiCalled = true;
                return Response(HttpStatusCode.OK, ReleaseJson);
            }

            return Response(
                HttpStatusCode.OK,
                "<li><a href=\"/yixing233/ImeTool/releases/download/v1.2.3/ImeTool_Windows_x64.exe\">ImeTool_Windows_x64.exe</a></li>" +
                $"<li><a href=\"/yixing233/ImeTool/releases/download/v1.2.3/other.zip\">other.zip</a><span>sha256:{unrelatedDigest}</span></li>");
        }));
        using var service = new GitHubUpdateService(client);

        UpdateCheckResult result = await service.CheckForUpdatesAsync(new Version(1, 0, 0));

        Assert.True(apiCalled);
        Assert.Equal(
            "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
            result.Release?.Sha256);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_FallsBackToApiWhenReleasePageCannotBeParsed()
    {
        using var client = new HttpClient(new RoutingHandler(request =>
            request.RequestUri!.AbsoluteUri == GitHubUpdateService.LatestReleaseApi
                ? Response(HttpStatusCode.OK, ReleaseJson)
                : Response(HttpStatusCode.OK, "<html>missing redirected release tag</html>")));
        using var service = new GitHubUpdateService(client);

        UpdateCheckResult result = await service.CheckForUpdatesAsync(new Version(1, 0, 0));

        Assert.Equal(UpdateAvailability.Available, result.Availability);
        Assert.Equal(new Version(1, 2, 3), result.Release?.Version);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_FallsBackWhenMetadataBodyTimesOut()
    {
        bool apiCalled = false;
        using var client = new HttpClient(new RoutingHandler(request =>
        {
            if (request.RequestUri!.AbsoluteUri == GitHubUpdateService.LatestReleasePage)
            {
                return Response(
                    HttpStatusCode.OK,
                    "<html>latest release</html>",
                    "https://github.com/yixing233/ImeTool/releases/tag/v1.2.3");
            }

            if (request.RequestUri.AbsoluteUri == GitHubUpdateService.LatestReleaseApi)
            {
                apiCalled = true;
                return Response(HttpStatusCode.OK, ReleaseJson);
            }

            return Response(HttpStatusCode.OK, new StreamContent(new BlockingReadStream()));
        }));
        using var service = new GitHubUpdateService(client, TimeSpan.FromMilliseconds(20));

        UpdateCheckResult result = await service.CheckForUpdatesAsync(new Version(1, 0, 0));

        Assert.True(apiCalled);
        Assert.Equal(UpdateAvailability.Available, result.Availability);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_DoesNotCallApiAfterCallerCancellation()
    {
        bool apiCalled = false;
        using var client = new HttpClient(new RoutingHandler(request =>
        {
            if (request.RequestUri!.AbsoluteUri == GitHubUpdateService.LatestReleasePage)
            {
                return Response(
                    HttpStatusCode.OK,
                    "<html>latest release</html>",
                    "https://github.com/yixing233/ImeTool/releases/tag/v1.2.3");
            }

            if (request.RequestUri.AbsoluteUri == GitHubUpdateService.LatestReleaseApi)
            {
                apiCalled = true;
                return Response(HttpStatusCode.OK, ReleaseJson);
            }

            return Response(HttpStatusCode.OK, new StreamContent(new BlockingReadStream()));
        }));
        using var service = new GitHubUpdateService(client, TimeSpan.FromSeconds(5));
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CheckForUpdatesAsync(new Version(1, 0, 0), cancellationSource.Token));

        Assert.False(apiCalled);
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

    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(route(request));
    }

    private static HttpResponseMessage Response(
        HttpStatusCode statusCode,
        string content,
        string? finalUri = null) => Response(statusCode, new StringContent(content), finalUri);

    private static HttpResponseMessage Response(
        HttpStatusCode statusCode,
        HttpContent content,
        string? finalUri = null) => new(statusCode)
    {
        Content = content,
        RequestMessage = new HttpRequestMessage(
            HttpMethod.Get,
            finalUri ?? "https://github.com/yixing233/ImeTool/releases/latest")
    };

    private sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
