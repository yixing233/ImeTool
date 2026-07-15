using ImeTool.Ime;

namespace ImeTool.State;

public sealed record WindowMemoryEntry(
    WindowKey Key,
    string Title,
    string ProcessName,
    bool Enabled,
    ImeOpenStatus Status,
    DateTimeOffset LastActivatedAt);

public interface IWindowMemorySource
{
    bool IsGlobalEnabled { get; }
    event EventHandler? EntriesChanged;
    IReadOnlyList<WindowMemoryEntry> GetEntries();
    void SetGlobalEnabled(bool enabled);
    void SetWindowEnabled(WindowKey key, bool enabled);
}

public sealed class WindowMemoryManager : IWindowMemorySource
{
    private readonly WindowStateStore _stateStore;
    private readonly Dictionary<WindowKey, WindowMemoryEntry> _entries = new();

    public WindowMemoryManager(WindowStateStore stateStore, bool globalEnabled)
    {
        _stateStore = stateStore;
        IsGlobalEnabled = globalEnabled;
    }

    public bool IsGlobalEnabled { get; private set; }

    public event EventHandler? EntriesChanged;

    public IReadOnlyList<WindowMemoryEntry> GetEntries() => _entries.Values
        .OrderByDescending(entry => entry.LastActivatedAt)
        .ThenBy(entry => entry.ProcessName, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    public bool CanTrack(WindowKey key) =>
        IsGlobalEnabled &&
        (!_entries.TryGetValue(key, out WindowMemoryEntry? entry) || entry.Enabled);

    public bool Contains(WindowKey key) => _entries.ContainsKey(key);

    public void ObserveWindow(
        WindowKey key,
        string? title,
        string? processName,
        DateTimeOffset activatedAt)
    {
        if (!IsGlobalEnabled || key.Hwnd == IntPtr.Zero || key.ProcessId == 0)
        {
            return;
        }

        string normalizedProcessName = string.IsNullOrWhiteSpace(processName)
            ? $"PID {key.ProcessId}"
            : processName.Trim();
        string normalizedTitle = string.IsNullOrWhiteSpace(title)
            ? normalizedProcessName
            : title.Trim();

        if (_entries.TryGetValue(key, out WindowMemoryEntry? existing))
        {
            WindowMemoryEntry updated = existing with
            {
                Title = normalizedTitle,
                ProcessName = normalizedProcessName,
                LastActivatedAt = activatedAt
            };
            if (updated == existing)
            {
                return;
            }

            _entries[key] = updated;
        }
        else
        {
            _entries[key] = new WindowMemoryEntry(
                key,
                normalizedTitle,
                normalizedProcessName,
                Enabled: true,
                ImeOpenStatus.Unknown,
                activatedAt);
        }

        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateStatus(WindowKey key, ImeOpenStatus status)
    {
        if (status == ImeOpenStatus.Unknown ||
            !CanTrack(key) ||
            !_entries.TryGetValue(key, out WindowMemoryEntry? existing) ||
            existing.Status == status)
        {
            return;
        }

        _entries[key] = existing with { Status = status };
        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetGlobalEnabled(bool enabled)
    {
        if (IsGlobalEnabled == enabled)
        {
            return;
        }

        IsGlobalEnabled = enabled;
        _stateStore.Clear();
        if (enabled)
        {
            foreach ((WindowKey key, WindowMemoryEntry entry) in _entries.ToArray())
            {
                _entries[key] = entry with { Status = ImeOpenStatus.Unknown };
            }
        }

        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetWindowEnabled(WindowKey key, bool enabled)
    {
        if (!_entries.TryGetValue(key, out WindowMemoryEntry? existing) || existing.Enabled == enabled)
        {
            return;
        }

        _stateStore.Remove(key);
        _entries[key] = existing with
        {
            Enabled = enabled,
            Status = enabled ? ImeOpenStatus.Unknown : existing.Status
        };
        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    public int Prune(Func<WindowKey, bool> isAlive)
    {
        WindowKey[] deadKeys = _entries.Keys.Where(key => !isAlive(key)).ToArray();
        foreach (WindowKey key in deadKeys)
        {
            _entries.Remove(key);
            _stateStore.Remove(key);
        }

        HashSet<IntPtr> aliveHandles = _entries.Keys.Select(key => key.Hwnd).ToHashSet();
        _stateStore.Prune(aliveHandles.Contains);
        if (deadKeys.Length != 0)
        {
            EntriesChanged?.Invoke(this, EventArgs.Empty);
        }

        return deadKeys.Length;
    }
}
