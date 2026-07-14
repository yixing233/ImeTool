using System.Diagnostics;

namespace ImeTool.Settings;

public readonly record struct ApplicationRuleMatch(bool Excluded, bool DisableStateRestore)
{
    public static ApplicationRuleMatch None => new(false, false);
}

public sealed class ApplicationRuleMatcher
{
    private readonly Dictionary<string, ApplicationRuleMatch> _rules;

    public ApplicationRuleMatcher(IReadOnlyList<ApplicationRule>? rules)
    {
        _rules = ApplicationRuleNormalizer.Normalize(rules)
            .ToDictionary(
                rule => rule.ProcessName,
                rule => new ApplicationRuleMatch(rule.Excluded, rule.DisableStateRestore),
                StringComparer.OrdinalIgnoreCase);
    }

    public bool HasRules => _rules.Count != 0;

    public ApplicationRuleMatch Match(string? processName)
    {
        string normalized = ApplicationRuleNormalizer.NormalizeProcessName(processName);
        return _rules.TryGetValue(normalized, out ApplicationRuleMatch match)
            ? match
            : ApplicationRuleMatch.None;
    }
}

public sealed class ProcessNameResolver
{
    private readonly Dictionary<uint, string?> _cache = new();

    public string? Resolve(uint processId)
    {
        if (processId == 0)
        {
            return null;
        }

        if (_cache.TryGetValue(processId, out string? cached))
        {
            return cached;
        }

        try
        {
            using Process process = Process.GetProcessById(checked((int)processId));
            string name = ApplicationRuleNormalizer.NormalizeProcessName(process.ProcessName);
            _cache[processId] = name;
            return name;
        }
        catch
        {
            _cache[processId] = null;
            return null;
        }
    }

    public void Clear() => _cache.Clear();
}
