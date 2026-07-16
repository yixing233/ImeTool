using System.Diagnostics;

namespace ImeTool.Settings;

public readonly record struct ApplicationRuleContext(
    string ProcessName,
    string WindowTitle,
    string WindowClass,
    string ControlClass);

public readonly record struct ApplicationRuleMatch(
    bool HideMarker,
    bool DisableWindowMemory,
    bool DisableStateRestore,
    int? OffsetX,
    int? OffsetY)
{
    public static ApplicationRuleMatch None => new(false, false, false, null, null);

    public bool HasMatch => HideMarker || DisableWindowMemory || DisableStateRestore ||
                            OffsetX is not null || OffsetY is not null;
}

public sealed class ApplicationRuleMatcher
{
    private readonly IReadOnlyList<ApplicationRule> _rules;

    public ApplicationRuleMatcher(IReadOnlyList<ApplicationRule>? rules)
    {
        _rules = ApplicationRuleNormalizer.Normalize(rules);
    }

    public bool HasRules => _rules.Count != 0;

    public ApplicationRuleMatch Match(string? processName) => Match(new ApplicationRuleContext(
        ApplicationRuleNormalizer.NormalizeProcessName(processName),
        string.Empty,
        string.Empty,
        string.Empty));

    public ApplicationRuleMatch Match(ApplicationRuleContext context)
    {
        string processName = ApplicationRuleNormalizer.NormalizeProcessName(context.ProcessName);
        bool hideMarker = false;
        bool disableWindowMemory = false;
        bool disableStateRestore = false;
        int? offsetX = null;
        int? offsetY = null;
        int offsetXSpecificity = -1;
        int offsetYSpecificity = -1;

        foreach (ApplicationRule rule in _rules)
        {
            if (!Matches(rule, processName, context))
            {
                continue;
            }

            hideMarker |= rule.HideMarker;
            disableWindowMemory |= rule.DisableWindowMemory;
            disableStateRestore |= rule.DisableStateRestore;
            int specificity = GetSpecificity(rule);
            if (rule.OffsetX is not null && specificity >= offsetXSpecificity)
            {
                offsetX = rule.OffsetX;
                offsetXSpecificity = specificity;
            }

            if (rule.OffsetY is not null && specificity >= offsetYSpecificity)
            {
                offsetY = rule.OffsetY;
                offsetYSpecificity = specificity;
            }
        }

        return new ApplicationRuleMatch(
            hideMarker,
            disableWindowMemory,
            disableStateRestore,
            offsetX,
            offsetY);
    }

    private static bool Matches(
        ApplicationRule rule,
        string processName,
        ApplicationRuleContext context) =>
        string.Equals(rule.ProcessName, processName, StringComparison.OrdinalIgnoreCase) &&
        (string.IsNullOrEmpty(rule.WindowTitleContains) ||
         context.WindowTitle.Contains(rule.WindowTitleContains, StringComparison.OrdinalIgnoreCase)) &&
        (string.IsNullOrEmpty(rule.WindowClass) ||
         string.Equals(rule.WindowClass, context.WindowClass, StringComparison.OrdinalIgnoreCase)) &&
        (string.IsNullOrEmpty(rule.ControlClass) ||
         string.Equals(rule.ControlClass, context.ControlClass, StringComparison.OrdinalIgnoreCase));

    private static int GetSpecificity(ApplicationRule rule) =>
        1 +
        (string.IsNullOrEmpty(rule.WindowTitleContains) ? 0 : 1) +
        (string.IsNullOrEmpty(rule.WindowClass) ? 0 : 1) +
        (string.IsNullOrEmpty(rule.ControlClass) ? 0 : 1);
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
