using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ImeTool.Updates;

public sealed record ReleaseHistoryEntry(
    string Version,
    string Date,
    string Notes)
{
    public string VersionLabel => $"v{Version}";
}

public static partial class ReleaseHistoryParser
{
    public static IReadOnlyList<ReleaseHistoryEntry> Parse(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return [];
        }

        MatchCollection matches = VersionHeadingRegex().Matches(markdown);
        var entries = new List<ReleaseHistoryEntry>(matches.Count);
        for (int index = 0; index < matches.Count; index++)
        {
            Match match = matches[index];
            int bodyStart = match.Index + match.Length;
            int bodyEnd = index + 1 < matches.Count ? matches[index + 1].Index : markdown.Length;
            string body = markdown[bodyStart..bodyEnd];
            entries.Add(new ReleaseHistoryEntry(
                match.Groups["version"].Value.Trim(),
                match.Groups["date"].Value.Trim(),
                ReleaseNotesFormatter.FromMarkdown(body)));
        }

        return entries;
    }

    [GeneratedRegex(
        @"^##\s+\[(?<version>[^\]]+)\](?:\s+-\s+(?<date>\d{4}-\d{2}-\d{2}))?\s*$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex VersionHeadingRegex();
}

public static class ReleaseHistoryCatalog
{
    private const string ResourceName = "ImeTool.CHANGELOG.md";

    public static IReadOnlyList<ReleaseHistoryEntry> LoadBundled()
    {
        Assembly assembly = typeof(ReleaseHistoryCatalog).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return [];
        }

        using var reader = new StreamReader(stream);
        return ReleaseHistoryParser.Parse(reader.ReadToEnd());
    }
}
