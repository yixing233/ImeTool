using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ImeTool.Updates;

public enum UpdateAvailability
{
    UpToDate,
    Available,
    NoPublishedRelease
}

public sealed record UpdateRelease(
    Version Version,
    string TagName,
    Uri DownloadUri,
    string Sha256,
    Uri ReleasePageUri,
    string ReleaseNotes);

public sealed record UpdateCheckResult(
    UpdateAvailability Availability,
    Version CurrentVersion,
    UpdateRelease? Release);

public static class AppVersion
{
    public static Version Current => Normalize(
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0));

    public static string Display => Format(Current);

    public static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        string trimmedTag = tag.Trim();
        if (trimmedTag.Length < 2 || trimmedTag[0] is not ('v' or 'V'))
        {
            return false;
        }

        string value = trimmedTag[1..];
        string[] parts = value.Split('.');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], out int major) ||
            !int.TryParse(parts[1], out int minor) ||
            !int.TryParse(parts[2], out int build) ||
            major < 0 || minor < 0 || build < 0)
        {
            return false;
        }

        version = new Version(major, minor, build);
        return true;
    }

    public static string Format(Version version) =>
        $"{Math.Max(0, version.Major)}.{Math.Max(0, version.Minor)}.{Math.Max(0, version.Build)}";

    private static Version Normalize(Version version) => new(
        Math.Max(0, version.Major),
        Math.Max(0, version.Minor),
        Math.Max(0, version.Build));
}

public static class AppPackage
{
    public const string WindowsX64AssetName = "ImeTool_Windows_x64.exe";

    public static string UpdateAssetName =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(
                attribute.Key,
                "UpdateAssetName",
                StringComparison.Ordinal))
            ?.Value ?? WindowsX64AssetName;

}

public sealed class GitHubUpdateService : IDisposable
{
    public const string LatestReleasePage = "https://github.com/yixing233/ImeTool/releases/latest";
    public const string LatestReleaseApi = "https://api.github.com/repos/yixing233/ImeTool/releases/latest";
    private const string ReleasesBaseUrl = "https://github.com/yixing233/ImeTool/releases";
    private const int MaxMetadataBytes = 2 * 1024 * 1024;
    private const long MaxExecutableBytes = 512L * 1024 * 1024;
    private static readonly TimeSpan DefaultMetadataReadTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _assetName;
    private readonly TimeSpan _metadataReadTimeout;

    public GitHubUpdateService(
        HttpClient? httpClient = null,
        TimeSpan? metadataReadTimeout = null)
    {
        _assetName = AppPackage.UpdateAssetName;
        _metadataReadTimeout = metadataReadTimeout ?? DefaultMetadataReadTimeout;
        if (_metadataReadTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(metadataReadTimeout),
                "元数据读取超时必须大于零。");
        }

        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ImeTool/{AppVersion.Display}");
        }

    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        Version? currentVersion = null,
        CancellationToken cancellationToken = default)
    {
        Version current = currentVersion is null
            ? AppVersion.Current
            : new Version(
                Math.Max(0, currentVersion.Major),
                Math.Max(0, currentVersion.Minor),
                Math.Max(0, currentVersion.Build));

        UpdateRelease? release;
        try
        {
            release = await GetLatestReleaseFromWebAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            release = await GetLatestReleaseFromApiAsync(cancellationToken);
        }

        if (release is null)
        {
            return new UpdateCheckResult(UpdateAvailability.NoPublishedRelease, current, null);
        }

        UpdateAvailability availability = release.Version > current
            ? UpdateAvailability.Available
            : UpdateAvailability.UpToDate;
        return new UpdateCheckResult(availability, current, release);
    }

    private async Task<UpdateRelease?> GetLatestReleaseFromWebAsync(CancellationToken cancellationToken)
    {
        using var latestRequest = new HttpRequestMessage(HttpMethod.Get, LatestReleasePage);
        latestRequest.Headers.Accept.ParseAdd("text/html");
        using HttpResponseMessage latestResponse = await _httpClient.SendAsync(
            latestRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (latestResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        latestResponse.EnsureSuccessStatusCode();
        Uri finalUri = latestResponse.RequestMessage?.RequestUri
            ?? throw new InvalidDataException("无法确定 GitHub 最新 Release 地址。");
        string tagName = ParseTagFromReleaseUri(finalUri);
        if (!AppVersion.TryParseTag(tagName, out Version version))
        {
            throw new InvalidDataException("GitHub Release 的版本标签无效。");
        }

        string expandedAssetsUrl = $"{ReleasesBaseUrl}/expanded_assets/{Uri.EscapeDataString(tagName)}";
        using var assetsRequest = new HttpRequestMessage(HttpMethod.Get, expandedAssetsUrl);
        assetsRequest.Headers.Accept.ParseAdd("text/html");
        using HttpResponseMessage assetsResponse = await _httpClient.SendAsync(
            assetsRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        assetsResponse.EnsureSuccessStatusCode();
        string html = await ReadLimitedStringAsync(assetsResponse.Content, cancellationToken);

        string expectedAssetPath = $"/yixing233/ImeTool/releases/download/{tagName}/{_assetName}";
        string digest = ParseAssetDigest(html, expectedAssetPath, _assetName);

        var releasePageUri = new Uri($"{ReleasesBaseUrl}/tag/{Uri.EscapeDataString(tagName)}");
        var downloadUri = new Uri(
            $"{ReleasesBaseUrl}/download/{Uri.EscapeDataString(tagName)}/{Uri.EscapeDataString(_assetName)}");
        return new UpdateRelease(
            version,
            tagName,
            downloadUri,
            digest,
            releasePageUri,
            string.Empty);
    }

    private async Task<UpdateRelease?> GetLatestReleaseFromApiAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        string json = await ReadLimitedStringAsync(response.Content, cancellationToken);
        return ParseRelease(json);
    }

    private static string ParseTagFromReleaseUri(Uri releaseUri)
    {
        Match match = Regex.Match(
            releaseUri.AbsolutePath,
            @"^/yixing233/ImeTool/releases/tag/(?<tag>[^/]+?)/?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success
            ? Uri.UnescapeDataString(match.Groups["tag"].Value)
            : throw new InvalidDataException("GitHub 最新 Release 未重定向到有效版本页面。");
    }

    private static string ParseAssetDigest(string html, string assetPath, string assetName)
    {
        string expectedHref = $"href=\"{assetPath}\"";
        int assetIndex = html.IndexOf(expectedHref, StringComparison.OrdinalIgnoreCase);
        if (assetIndex < 0)
        {
            throw new InvalidDataException($"Release 缺少 {assetName}。");
        }

        if (html.IndexOf(expectedHref, assetIndex + expectedHref.Length, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            throw new InvalidDataException($"Release 包含多个 {assetName} 资源。");
        }

        int rowStart = FindAssetRowStart(html, assetIndex);
        int rowEnd = html.IndexOf("</li>", assetIndex, StringComparison.OrdinalIgnoreCase);
        if (rowStart < 0 || rowEnd < 0)
        {
            throw new InvalidDataException("GitHub Release 资源列表格式无效。");
        }

        string assetRow = html[rowStart..(rowEnd + "</li>".Length)];
        string[] digests = Regex.Matches(
                assetRow,
                @"sha256:[a-fA-F0-9]{64}",
                RegexOptions.CultureInvariant)
            .Select(match => ParseDigest(match.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return digests.Length == 1
            ? digests[0]
            : throw new InvalidDataException("Release 资源缺少唯一的 SHA-256 digest。");
    }

    private static int FindAssetRowStart(string html, int assetIndex)
    {
        int searchIndex = assetIndex;
        while (searchIndex > 0)
        {
            int candidate = html.LastIndexOf("<li", searchIndex, StringComparison.OrdinalIgnoreCase);
            if (candidate < 0)
            {
                return -1;
            }

            int suffixIndex = candidate + 3;
            if (suffixIndex < html.Length &&
                (html[suffixIndex] == '>' || char.IsWhiteSpace(html[suffixIndex])))
            {
                return candidate;
            }

            searchIndex = candidate - 1;
        }

        return -1;
    }

    private async Task<string> ReadLimitedStringAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaxMetadataBytes)
        {
            throw new InvalidDataException("GitHub Release 元数据超过大小限制。");
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_metadataReadTimeout);
        CancellationToken readToken = timeoutSource.Token;
        try
        {
            await using Stream source = await content.ReadAsStreamAsync(readToken);
            using var destination = new MemoryStream();
            byte[] buffer = new byte[81920];
            while (true)
            {
                int read = await source.ReadAsync(buffer, readToken);
                if (read == 0)
                {
                    break;
                }

                if (destination.Length + read > MaxMetadataBytes)
                {
                    throw new InvalidDataException("GitHub Release 元数据超过大小限制。");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), readToken);
            }

            return System.Text.Encoding.UTF8.GetString(
                destination.GetBuffer(),
                0,
                checked((int)destination.Length));
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("读取 GitHub Release 元数据超时。", exception);
        }
    }

    public async Task<string> DownloadUpdateAsync(
        UpdateRelease release,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string expectedChecksum = release.Sha256;
        string updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImeTool",
            "Updates",
            release.TagName);
        Directory.CreateDirectory(updateDirectory);
        string destinationPath = Path.Combine(updateDirectory, _assetName + ".download");
        string installerPath = Path.Combine(updateDirectory, _assetName);

        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(
                release.DownloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            long? contentLength = response.Content.Headers.ContentLength;
            if (contentLength > MaxExecutableBytes)
            {
                throw new InvalidDataException("更新文件超过允许的大小限制。");
            }

            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[81920];
            long totalRead = 0;
            while (true)
            {
                int read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (totalRead + read > MaxExecutableBytes)
                {
                    throw new InvalidDataException("更新文件超过允许的大小限制。");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                hasher.AppendData(buffer, 0, read);
                totalRead += read;
                if (contentLength is > 0)
                {
                    progress?.Report(Math.Clamp((double)totalRead / contentLength.Value, 0, 1));
                }
            }

            await destination.FlushAsync(cancellationToken);
            string actualChecksum = Convert.ToHexString(hasher.GetHashAndReset());
            if (!string.Equals(actualChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("下载文件的 SHA-256 校验失败。");
            }

            progress?.Report(1);
            await destination.DisposeAsync();
            ValidateInstallerPayload(destinationPath);
            File.Move(destinationPath, installerPath, overwrite: true);
            return installerPath;
        }
        catch
        {
            File.Delete(destinationPath);
            File.Delete(installerPath);
            throw;
        }
    }

    public static UpdateRelease ParseRelease(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string tagName = root.GetProperty("tag_name").GetString() ?? string.Empty;
        if (!AppVersion.TryParseTag(tagName, out Version version))
        {
            throw new InvalidDataException("GitHub Release 的版本标签无效。");
        }

        string releasePage = root.GetProperty("html_url").GetString() ?? string.Empty;
        string notes = root.TryGetProperty("body", out JsonElement body)
            ? body.GetString() ?? string.Empty
            : string.Empty;
        Uri? downloadUri = null;
        string? sha256 = null;
        foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? string.Empty;
            string url = asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
            if (string.Equals(name, AppPackage.UpdateAssetName, StringComparison.OrdinalIgnoreCase))
            {
                downloadUri = CreateAbsoluteUri(url);
                string digest = asset.TryGetProperty("digest", out JsonElement digestElement)
                    ? digestElement.GetString() ?? string.Empty
                    : string.Empty;
                sha256 = ParseDigest(digest);
            }
        }

        return new UpdateRelease(
            version,
            tagName,
            downloadUri ?? throw new InvalidDataException($"Release 缺少 {AppPackage.UpdateAssetName}。"),
            sha256 ?? throw new InvalidDataException("Release 资源缺少 SHA-256 digest。"),
            CreateAbsoluteUri(releasePage),
            notes);
    }

    public static string ParseDigest(string content)
    {
        const string prefix = "sha256:";
        string value = (content ?? string.Empty).Trim();
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("GitHub Release 资源缺少 SHA-256 digest。");
        }

        string candidate = value[prefix.Length..];
        if (candidate.Length != 64 || !candidate.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("GitHub Release 资源的 SHA-256 digest 无效。");
        }

        return candidate.ToUpperInvariant();
    }

    public static string ValidateInstallerPayload(string installerPath)
    {
        var file = new FileInfo(installerPath);
        if (!file.Exists || file.Length <= 2 || file.Length > MaxExecutableBytes)
        {
            throw new InvalidDataException("更新安装包大小无效。");
        }

        using var header = new FileStream(installerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (header.ReadByte() != 'M' || header.ReadByte() != 'Z')
        {
            throw new InvalidDataException("更新安装包格式无效。");
        }

        return installerPath;
    }

    private static Uri CreateAbsoluteUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            ? uri
            : throw new InvalidDataException("GitHub Release 包含无效下载地址。");

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
