using ImeTool.Ime;

namespace ImeTool.State;

public sealed record WindowMemoryEntry(
    WindowKey Key,
    string Title,
    string ProcessName,
    bool Enabled,
    ImeOpenStatus Status,
    DateTimeOffset LastActivatedAt);

public readonly record struct WindowMemoryObservationResult(bool Added, bool HasPersistedState)
{
    public static WindowMemoryObservationResult None => new(false, false);
}

internal readonly record struct PersistentWindowIdentity(string ProcessName, string Title)
{
    public static PersistentWindowIdentity Create(string processName, string title) => new(
        processName.Trim().ToUpperInvariant(),
        title.Trim().ToUpperInvariant());
}

public interface IWindowMemorySource
{
    bool IsGlobalEnabled { get; }
    string? PersistenceError { get; }
    event EventHandler? EntriesChanged;
    IReadOnlyList<WindowMemoryEntry> GetEntries();
    void SetGlobalEnabled(bool enabled);
    void SetWindowEnabled(WindowKey key, bool enabled);
}

public sealed class WindowMemoryManager : IWindowMemorySource
{
    private readonly WindowStateStore _stateStore;
    private readonly Dictionary<WindowKey, WindowMemoryEntry> _entries = new();
    private readonly Dictionary<WindowKey, PersistentWindowIdentity> _identities = new();
    private Dictionary<PersistentWindowIdentity, PersistedWindowMemoryEntry> _persistedProfiles = new();
    private IWindowMemoryPersistenceStore? _persistenceStore;

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

    public bool IsPersistenceEnabled => _persistenceStore is not null;

    public string? PersistencePath => _persistenceStore?.StoragePath;

    public string? PersistenceError { get; private set; }

    public void ConfigurePersistence(bool enabled, IWindowMemoryPersistenceStore? persistenceStore)
    {
        if (!enabled || persistenceStore is null)
        {
            _persistenceStore = null;
            _persistedProfiles.Clear();
            PersistenceError = null;
            return;
        }

        IWindowMemoryPersistenceStore? previousStore = _persistenceStore;
        bool retryFailedSave = !string.IsNullOrWhiteSpace(PersistenceError);
        Dictionary<PersistentWindowIdentity, PersistedWindowMemoryEntry> loaded = ToProfileMap(persistenceStore.Load());
        if (previousStore is not null)
        {
            foreach ((PersistentWindowIdentity identity, PersistedWindowMemoryEntry profile) in _persistedProfiles)
            {
                if (!loaded.TryGetValue(identity, out PersistedWindowMemoryEntry? existing) ||
                    profile.UpdatedAt > existing.UpdatedAt)
                {
                    loaded[identity] = profile;
                }
            }
        }

        _persistenceStore = persistenceStore;
        _persistedProfiles = loaded;
        bool needsSave = retryFailedSave ||
                         (previousStore is not null &&
                          !string.Equals(previousStore.StoragePath, persistenceStore.StoragePath, StringComparison.OrdinalIgnoreCase));
        if (!needsSave)
        {
            PersistenceError = null;
        }
        foreach ((WindowKey key, WindowMemoryEntry entry) in _entries.ToArray())
        {
            if (!_identities.TryGetValue(key, out PersistentWindowIdentity identity))
            {
                identity = PersistentWindowIdentity.Create(entry.ProcessName, entry.Title);
                _identities[key] = identity;
            }

            if (_persistedProfiles.TryGetValue(identity, out PersistedWindowMemoryEntry? profile))
            {
                ApplyProfileToRuntimeEntry(key, entry, profile);
            }
            else
            {
                PersistRuntimeEntry(entry, saveImmediately: false);
                needsSave = true;
            }
        }

        if (needsSave)
        {
            SaveProfiles();
        }

        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    public WindowMemoryObservationResult ObserveWindow(
        WindowKey key,
        string? title,
        string? processName,
        DateTimeOffset activatedAt)
    {
        if (!IsGlobalEnabled || key.Hwnd == IntPtr.Zero || key.ProcessId == 0)
        {
            return WindowMemoryObservationResult.None;
        }

        string normalizedProcessName = string.IsNullOrWhiteSpace(processName)
            ? $"PID {key.ProcessId}"
            : processName.Trim();
        string normalizedTitle = string.IsNullOrWhiteSpace(title)
            ? normalizedProcessName
            : title.Trim();

        if (_entries.TryGetValue(key, out WindowMemoryEntry? existing))
        {
            PersistentWindowIdentity newIdentity = PersistentWindowIdentity.Create(normalizedProcessName, normalizedTitle);
            _identities.TryGetValue(key, out PersistentWindowIdentity oldIdentity);
            WindowMemoryEntry updated = existing with
            {
                Title = normalizedTitle,
                ProcessName = normalizedProcessName,
                LastActivatedAt = activatedAt
            };
            if (updated == existing)
            {
                return WindowMemoryObservationResult.None;
            }

            _entries[key] = updated;
            if (oldIdentity != newIdentity)
            {
                _identities[key] = newIdentity;
                if (_persistedProfiles.TryGetValue(newIdentity, out PersistedWindowMemoryEntry? targetProfile))
                {
                    ApplyProfileToRuntimeEntry(key, updated, targetProfile);
                    EntriesChanged?.Invoke(this, EventArgs.Empty);
                    return new WindowMemoryObservationResult(
                        Added: false,
                        HasPersistedState: targetProfile.Enabled && targetProfile.IsImeOpen.HasValue);
                }

                bool oldIdentityStillUsed = _identities.Any(pair => pair.Key != key && pair.Value == oldIdentity);
                if (!oldIdentityStillUsed)
                {
                    _persistedProfiles.Remove(oldIdentity);
                }
                PersistRuntimeEntry(updated);
            }

            EntriesChanged?.Invoke(this, EventArgs.Empty);
            return WindowMemoryObservationResult.None;
        }
        else
        {
            PersistentWindowIdentity identity = PersistentWindowIdentity.Create(normalizedProcessName, normalizedTitle);
            _persistedProfiles.TryGetValue(identity, out PersistedWindowMemoryEntry? persisted);
            bool enabled = persisted?.Enabled ?? true;
            ImeOpenStatus status = persisted?.IsImeOpen is bool savedIsOpen
                ? ImeOpenStatusExtensions.FromBool(savedIsOpen)
                : ImeOpenStatus.Unknown;
            _entries[key] = new WindowMemoryEntry(
                key,
                normalizedTitle,
                normalizedProcessName,
                enabled,
                status,
                activatedAt);
            _identities[key] = identity;

            bool? persistedState = persisted?.IsImeOpen;
            bool hasPersistedState = enabled && persistedState.HasValue;
            if (hasPersistedState)
            {
                _stateStore.Save(key, persistedState!.Value);
            }
            else if (_persistenceStore is not null && persisted is null)
            {
                PersistRuntimeEntry(_entries[key]);
            }

            EntriesChanged?.Invoke(this, EventArgs.Empty);
            return new WindowMemoryObservationResult(true, hasPersistedState);
        }
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
        PersistRuntimeEntry(_entries[key]);
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
                WindowMemoryEntry updated = entry with { Status = ImeOpenStatus.Unknown };
                if (_identities.TryGetValue(key, out PersistentWindowIdentity identity) &&
                    _persistedProfiles.TryGetValue(identity, out PersistedWindowMemoryEntry? persisted))
                {
                    updated = updated with
                    {
                        Enabled = persisted.Enabled,
                        Status = persisted.IsImeOpen is bool savedIsOpen
                            ? ImeOpenStatusExtensions.FromBool(savedIsOpen)
                            : ImeOpenStatus.Unknown
                    };
                    if (updated.Enabled && persisted.IsImeOpen is bool isOpen)
                    {
                        _stateStore.Save(key, isOpen);
                    }
                }

                _entries[key] = updated;
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
        PersistRuntimeEntry(_entries[key]);
        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    public int Prune(Func<WindowKey, bool> isAlive)
    {
        WindowKey[] deadKeys = _entries.Keys.Where(key => !isAlive(key)).ToArray();
        foreach (WindowKey key in deadKeys)
        {
            _entries.Remove(key);
            _identities.Remove(key);
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

    private void PersistRuntimeEntry(WindowMemoryEntry entry, bool saveImmediately = true)
    {
        if (_persistenceStore is null ||
            !_identities.TryGetValue(entry.Key, out PersistentWindowIdentity identity) ||
            IsIdentityAmbiguous(identity))
        {
            return;
        }

        _persistedProfiles[identity] = new PersistedWindowMemoryEntry(
            entry.ProcessName,
            entry.Title,
            entry.Enabled,
            entry.Status.ToNullableBool(),
            DateTimeOffset.UtcNow);
        if (saveImmediately)
        {
            SaveProfiles();
        }
    }

    private bool IsIdentityAmbiguous(PersistentWindowIdentity identity) =>
        _identities.Values.Count(candidate => candidate == identity) > 1;

    private void SaveProfiles()
    {
        if (_persistenceStore is null)
        {
            PersistenceError = null;
            return;
        }

        if (_persistenceStore.TrySave(_persistedProfiles.Values.ToArray(), out string? error))
        {
            PersistenceError = null;
        }
        else
        {
            PersistenceError = string.IsNullOrWhiteSpace(error) ? "窗口记忆写入失败" : error;
        }
    }

    private void ApplyProfileToRuntimeEntry(
        WindowKey key,
        WindowMemoryEntry entry,
        PersistedWindowMemoryEntry profile)
    {
        WindowMemoryEntry updated = entry with
        {
            Enabled = profile.Enabled,
            Status = profile.IsImeOpen is bool savedIsOpen
                ? ImeOpenStatusExtensions.FromBool(savedIsOpen)
                : ImeOpenStatus.Unknown
        };
        _entries[key] = updated;
        _stateStore.Remove(key);
        if (updated.Enabled && profile.IsImeOpen is bool isOpen)
        {
            _stateStore.Save(key, isOpen);
        }
    }

    private static Dictionary<PersistentWindowIdentity, PersistedWindowMemoryEntry> ToProfileMap(
        IReadOnlyList<PersistedWindowMemoryEntry> entries)
    {
        var result = new Dictionary<PersistentWindowIdentity, PersistedWindowMemoryEntry>();
        foreach (PersistedWindowMemoryEntry entry in entries.OrderBy(entry => entry.UpdatedAt))
        {
            if (string.IsNullOrWhiteSpace(entry.ProcessName) || string.IsNullOrWhiteSpace(entry.Title))
            {
                continue;
            }

            result[PersistentWindowIdentity.Create(entry.ProcessName, entry.Title)] = entry;
        }

        return result;
    }
}
