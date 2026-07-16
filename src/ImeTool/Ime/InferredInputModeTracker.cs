using ImeTool.State;

namespace ImeTool.Ime;

public sealed class InferredInputModeTracker
{
    private readonly Dictionary<WindowKey, Entry> _entries = new();

    public TextInputMode Resolve(WindowKey key, TextInputMode reportedMode)
    {
        if (reportedMode == TextInputMode.Unknown)
        {
            return _entries.TryGetValue(key, out Entry existing)
                ? existing.EffectiveMode
                : TextInputMode.Unknown;
        }

        if (!_entries.TryGetValue(key, out Entry entry) || entry.ReportedMode != reportedMode)
        {
            entry = new Entry(reportedMode, reportedMode, HasEffectiveOverride: false);
            _entries[key] = entry;
        }

        return entry.EffectiveMode;
    }

    public void SetEffectiveMode(WindowKey key, TextInputMode mode)
    {
        if (mode == TextInputMode.Unknown)
        {
            return;
        }

        if (_entries.TryGetValue(key, out Entry entry))
        {
            _entries[key] = entry with { EffectiveMode = mode, HasEffectiveOverride = true };
        }
        else
        {
            _entries[key] = new Entry(TextInputMode.Unknown, mode, HasEffectiveOverride: true);
        }
    }

    public bool HasEffectiveOverride(WindowKey key) =>
        _entries.TryGetValue(key, out Entry entry) && entry.HasEffectiveOverride;

    public int Prune(Func<IntPtr, bool> isAlive)
    {
        WindowKey[] dead = _entries.Keys.Where(key => !isAlive(key.Hwnd)).ToArray();
        foreach (WindowKey key in dead)
        {
            _entries.Remove(key);
        }

        return dead.Length;
    }

    private readonly record struct Entry(
        TextInputMode ReportedMode,
        TextInputMode EffectiveMode,
        bool HasEffectiveOverride);
}
