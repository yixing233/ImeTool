using System.Net;
using System.Text.RegularExpressions;

namespace ImeTool.Updates;

public static class ReleaseNotesFormatter
{
    private const int MaximumLength = 12000;
    private const string EmptyNotesText = "此版本未提供更新说明。";

    public static string FromMarkdown(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return EmptyNotesText;
        }

        string value = NormalizeLineEndings(markdown);
        value = Regex.Replace(value, @"!\[(?<text>[^\]]*)\]\([^\)]*\)", "${text}");
        value = Regex.Replace(value, @"\[(?<text>[^\]]+)\]\([^\)]*\)", "${text}");
        value = Regex.Replace(value, @"^[ \t]{0,3}#{1,6}[ \t]+", string.Empty, RegexOptions.Multiline);
        value = Regex.Replace(value, @"^[ \t]*[-*+][ \t]+", "• ", RegexOptions.Multiline);
        value = Regex.Replace(value, @"^[ \t]*[-*_]{3,}[ \t]*$", string.Empty, RegexOptions.Multiline);
        value = value.Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal);
        value = Regex.Replace(value, @"<[^>]+>", string.Empty);
        return FinalizeText(WebUtility.HtmlDecode(value));
    }

    public static string FromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return EmptyNotesText;
        }

        string value = NormalizeLineEndings(html);
        value = Regex.Replace(value, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"<\s*li(?:\s[^>]*)?>", "• ", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"<\s*/\s*li\s*>", "\n", RegexOptions.IgnoreCase);
        value = Regex.Replace(
            value,
            @"<\s*/\s*(?:p|div|h[1-6])\s*>",
            "\n\n",
            RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"<[^>]+>", string.Empty);
        return FinalizeText(WebUtility.HtmlDecode(value));
    }

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static string FinalizeText(string value)
    {
        string[] lines = NormalizeLineEndings(value)
            .Split('\n')
            .Select(line => line.TrimEnd())
            .ToArray();
        string result = string.Join('\n', lines).Trim();
        result = Regex.Replace(result, @"\n{3,}", "\n\n");
        if (string.IsNullOrWhiteSpace(result))
        {
            return EmptyNotesText;
        }

        return result.Length <= MaximumLength
            ? result
            : result[..MaximumLength].TrimEnd() + "…";
    }
}
