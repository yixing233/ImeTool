using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

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
    public const string WindowsX64AssetName = "ImeTool_Windows_x64.zip";

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
    public const string LatestReleaseApi = "https://api.github.com/repos/yixing233/ImeTool/releases/latest";
    private const long MaxExecutableBytes = 512L * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _assetName;

    public GitHubUpdateService(HttpClient? httpClient = null)
    {
        _assetName = AppPackage.UpdateAssetName;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ImeTool/{AppVersion.Display}");
        }

        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
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

        using HttpResponseMessage response = await _httpClient.GetAsync(LatestReleaseApi, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new UpdateCheckResult(UpdateAvailability.NoPublishedRelease, current, null);
        }

        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        UpdateRelease release = ParseRelease(json);
        UpdateAvailability availability = release.Version > current
            ? UpdateAvailability.Available
            : UpdateAvailability.UpToDate;
        return new UpdateCheckResult(availability, current, release);
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
            return ExtractUpdatePayload(destinationPath);
        }
        catch
        {
            File.Delete(destinationPath);
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

    public static string ExtractUpdatePayload(string downloadedPath)
    {
        bool isZipArchive = downloadedPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                            downloadedPath.EndsWith(".zip.download", StringComparison.OrdinalIgnoreCase);
        if (!isZipArchive)
        {
            return downloadedPath;
        }

        string extractedPath = Path.Combine(
            Path.GetDirectoryName(downloadedPath) ?? Path.GetTempPath(),
            "ImeTool-update-extracted.exe");
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(downloadedPath);
            ZipArchiveEntry[] executableEntries = archive.Entries
                .Where(entry => string.Equals(entry.FullName, "ImeTool.exe", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (executableEntries.Length != 1)
            {
                throw new InvalidDataException("更新包中缺少唯一的 ImeTool.exe。");
            }

            ZipArchiveEntry executable = executableEntries[0];
            if (executable.Length is <= 2 or > MaxExecutableBytes)
            {
                throw new InvalidDataException("更新包中的程序文件大小无效。");
            }

            using Stream source = executable.Open();
            using var destination = new FileStream(extractedPath, FileMode.Create, FileAccess.Write, FileShare.None);
            byte[] buffer = new byte[81920];
            long totalRead = 0;
            while (true)
            {
                int read = source.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
                if (totalRead > MaxExecutableBytes)
                {
                    throw new InvalidDataException("更新包中的程序文件超过大小限制。");
                }

                destination.Write(buffer, 0, read);
            }

            destination.Flush();
            if (totalRead != executable.Length)
            {
                throw new InvalidDataException("更新包中的程序文件不完整。");
            }

            destination.Dispose();
            using var header = new FileStream(extractedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (header.ReadByte() != 'M' || header.ReadByte() != 'Z')
            {
                throw new InvalidDataException("更新包中的程序文件格式无效。");
            }
        }
        catch
        {
            File.Delete(extractedPath);
            throw;
        }

        File.Delete(downloadedPath);
        return extractedPath;
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
