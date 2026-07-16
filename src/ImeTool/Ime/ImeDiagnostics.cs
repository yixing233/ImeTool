using ImeTool.Settings;

namespace ImeTool.Ime;

public enum ImeDetectionSource
{
    Unknown = 0,
    CustomRule = 1,
    ConversionMode = 2,
    OpenStatus = 3,
    Context = 4,
    Fallback = 5,
    NonChineseLayout = 6
}

public sealed record ImeDiagnosticSnapshot
{
    public IntPtr FocusHwnd { get; init; }
    public uint ProcessId { get; init; }
    public uint ThreadId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string WindowTitle { get; init; } = string.Empty;
    public string WindowClass { get; init; } = string.Empty;
    public string ControlClass { get; init; } = string.Empty;
    public string CaretSource { get; init; } = string.Empty;
    public string KeyboardLayout { get; init; } = string.Empty;
    public ushort LanguageId { get; init; }
    public bool IsChineseInputMethod { get; init; }
    public long? OpenStatusCode { get; init; }
    public uint? ConversionMode { get; init; }
    public TextInputMode ContextMode { get; init; }
    public ImeOpenStatus OpenStatus { get; init; }
    public TextInputMode FinalMode { get; init; }
    public ImeDetectionSource Source { get; init; }
    public string? MatchedRuleDescription { get; init; }
}

public interface IImeDiagnosticService
{
    ImeDiagnosticSnapshot ReadDiagnostics(IntPtr hwnd);
    void SetDetectionRules(IReadOnlyList<ImeDetectionRule> rules);
}

public interface IImeDiagnosticsSource
{
    ImeDiagnosticSnapshot? CurrentSnapshot { get; }
    event Action<ImeDiagnosticSnapshot>? SnapshotChanged;
}

public sealed class ImeDiagnosticsState : IImeDiagnosticsSource
{
    public ImeDiagnosticSnapshot? CurrentSnapshot { get; private set; }

    public event Action<ImeDiagnosticSnapshot>? SnapshotChanged;

    public void Update(ImeDiagnosticSnapshot snapshot)
    {
        if (CurrentSnapshot == snapshot)
        {
            return;
        }

        CurrentSnapshot = snapshot;
        SnapshotChanged?.Invoke(snapshot);
    }
}

public sealed class ImeDetectionRuleMatcher
{
    private readonly IReadOnlyList<ImeDetectionRule> _rules;

    public ImeDetectionRuleMatcher(IReadOnlyList<ImeDetectionRule>? rules)
    {
        _rules = ImeDetectionRuleNormalizer.Normalize(rules);
    }

    public ImeDetectionRule? Match(string keyboardLayout, long? openStatusCode, uint? conversionMode)
    {
        ImeDetectionRule? best = null;
        int bestSpecificity = -1;
        foreach (ImeDetectionRule rule in _rules)
        {
            if (rule.KeyboardLayout != "*" &&
                !string.Equals(rule.KeyboardLayout, keyboardLayout, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (rule.OpenStatusCode is long expectedOpen && expectedOpen != openStatusCode)
            {
                continue;
            }

            if (rule.ConversionMode is uint expectedConversion && expectedConversion != conversionMode)
            {
                continue;
            }

            int specificity = (rule.KeyboardLayout == "*" ? 0 : 1) +
                              (rule.OpenStatusCode.HasValue ? 1 : 0) +
                              (rule.ConversionMode.HasValue ? 1 : 0);
            if (specificity >= bestSpecificity)
            {
                best = rule;
                bestSpecificity = specificity;
            }
        }

        return best;
    }
}
