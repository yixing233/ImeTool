using System.IO;
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
    Uri ChecksumUri,
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

        string value = tag.Trim().TrimStart('v', 'V');
        int suffixIndex = value.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
        {
            value = value[..suffixIndex];
        }

        if (!Version.TryParse(value, out Version? parsed))
        {
            return false;
        }

        version = Normalize(parsed);
        return true;
    }

    public static string Format(Version version) =>
        $"{Math.Max(0, version.Major)}.{Math.Max(0, version.Minor)}.{Math.Max(0, version.Build)}";

    private static Version Normalize(Version version) => new(
        Math.Max(0, version.Major),
        Math.Max(0, version.Minor),
        Math.Max(0, version.Build));
}

public sealed class GitHubUpdateService : IDisposable
{
    public const string LatestReleaseApi = "https://api.github.com/repos/yixing233/ImeTool/releases/latest";
    public const string ExecutableAssetName = "ImeTool-win-x64.exe";
    public const string ChecksumAssetName = "ImeTool-win-x64.exe.sha256";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public GitHubUpdateService(HttpClient? httpClient = null)
    {
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
        string checksumText = await _httpClient.GetStringAsync(release.ChecksumUri, cancellationToken);
        string expectedChecksum = ParseChecksum(checksumText);
        string updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImeTool",
            "Updates",
            release.TagName);
        Directory.CreateDirectory(updateDirectory);
        string destinationPath = Path.Combine(updateDirectory, ExecutableAssetName + ".download");

        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(
                release.DownloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            long? contentLength = response.Content.Headers.ContentLength;
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
            return destinationPath;
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
        Uri? executableUri = null;
        Uri? checksumUri = null;
        foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? string.Empty;
            string url = asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
            if (string.Equals(name, ExecutableAssetName, StringComparison.OrdinalIgnoreCase))
            {
                executableUri = CreateAbsoluteUri(url);
            }
            else if (string.Equals(name, ChecksumAssetName, StringComparison.OrdinalIgnoreCase))
            {
                checksumUri = CreateAbsoluteUri(url);
            }
        }

        return new UpdateRelease(
            version,
            tagName,
            executableUri ?? throw new InvalidDataException($"Release 缺少 {ExecutableAssetName}。"),
            checksumUri ?? throw new InvalidDataException($"Release 缺少 {ChecksumAssetName}。"),
            CreateAbsoluteUri(releasePage),
            notes);
    }

    public static string ParseChecksum(string content)
    {
        string candidate = (content ?? string.Empty).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        if (candidate.Length != 64 || !candidate.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Release 中的 SHA-256 校验文件无效。");
        }

        return candidate.ToUpperInvariant();
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
