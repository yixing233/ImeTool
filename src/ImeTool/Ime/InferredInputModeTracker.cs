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
            entry = new Entry(reportedMode, reportedMode);
            _entries[key] = entry;
        }

        return entry.EffectiveMode;
    }

    public TextInputMode Toggle(WindowKey key)
    {
        if (!_entries.TryGetValue(key, out Entry entry))
        {
            return TextInputMode.Unknown;
        }

        TextInputMode toggled = entry.EffectiveMode switch
        {
            TextInputMode.Chinese => TextInputMode.English,
            TextInputMode.English => TextInputMode.Chinese,
            _ => TextInputMode.Unknown
        };
        _entries[key] = entry with { EffectiveMode = toggled };
        return toggled;
    }

    public int Prune(Func<IntPtr, bool> isAlive)
    {
        WindowKey[] dead = _entries.Keys.Where(key => !isAlive(key.Hwnd)).ToArray();
        foreach (WindowKey key in dead)
        {
            _entries.Remove(key);
        }

        return dead.Length;
    }

    private readonly record struct Entry(TextInputMode ReportedMode, TextInputMode EffectiveMode);
}
