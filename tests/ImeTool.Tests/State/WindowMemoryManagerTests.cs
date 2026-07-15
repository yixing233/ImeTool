using ImeTool.Ime;
using ImeTool.State;

namespace ImeTool.Tests.State;

public sealed class WindowMemoryManagerTests
{
    [Fact]
    public void Observe_Adds_Window_And_Updates_Metadata()
    {
        var manager = new WindowMemoryManager(new WindowStateStore(), globalEnabled: true);
        var key = new WindowKey(new IntPtr(101), 2001);
        DateTimeOffset firstSeen = new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);

        manager.ObserveWindow(key, "Untitled - Notepad", "Notepad", firstSeen);
        manager.ObserveWindow(key, "notes.txt - Notepad", "Notepad", firstSeen.AddMinutes(1));

        WindowMemoryEntry entry = Assert.Single(manager.GetEntries());
        Assert.Equal(key, entry.Key);
        Assert.Equal("notes.txt - Notepad", entry.Title);
        Assert.Equal("Notepad", entry.ProcessName);
        Assert.True(entry.Enabled);
        Assert.Equal(ImeOpenStatus.Unknown, entry.Status);
        Assert.Equal(firstSeen.AddMinutes(1), entry.LastActivatedAt);
    }

    [Fact]
    public void Entries_Are_Independent_And_Sorted_By_Last_Activation()
    {
        var manager = new WindowMemoryManager(new WindowStateStore(), globalEnabled: true);
        var first = new WindowKey(new IntPtr(101), 2001);
        var second = new WindowKey(new IntPtr(102), 2001);
        DateTimeOffset now = new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);

        manager.ObserveWindow(first, "First", "App", now);
        manager.ObserveWindow(second, "Second", "App", now.AddSeconds(1));
        manager.UpdateStatus(first, ImeOpenStatus.Open);
        manager.UpdateStatus(second, ImeOpenStatus.Closed);

        IReadOnlyList<WindowMemoryEntry> entries = manager.GetEntries();
        Assert.Equal([second, first], entries.Select(entry => entry.Key));
        Assert.Equal(ImeOpenStatus.Closed, entries[0].Status);
        Assert.Equal(ImeOpenStatus.Open, entries[1].Status);
    }

    [Fact]
    public void Disabling_Window_Stops_Tracking_And_Removes_Saved_State()
    {
        var store = new WindowStateStore();
        var manager = new WindowMemoryManager(store, globalEnabled: true);
        var key = new WindowKey(new IntPtr(101), 2001);
        manager.ObserveWindow(key, "Editor", "Editor", DateTimeOffset.UtcNow);
        manager.UpdateStatus(key, ImeOpenStatus.Open);
        store.Save(key, true);

        manager.SetWindowEnabled(key, false);
        manager.UpdateStatus(key, ImeOpenStatus.Closed);

        Assert.False(manager.CanTrack(key));
        Assert.False(store.TryGet(key, out _));
        WindowMemoryEntry entry = Assert.Single(manager.GetEntries());
        Assert.False(entry.Enabled);
        Assert.Equal(ImeOpenStatus.Open, entry.Status);
    }

    [Fact]
    public void Reenabling_Window_Starts_With_Unknown_State_And_Allows_Tracking()
    {
        var manager = new WindowMemoryManager(new WindowStateStore(), globalEnabled: true);
        var key = new WindowKey(new IntPtr(101), 2001);
        manager.ObserveWindow(key, "Editor", "Editor", DateTimeOffset.UtcNow);
        manager.UpdateStatus(key, ImeOpenStatus.Open);
        manager.SetWindowEnabled(key, false);

        manager.SetWindowEnabled(key, true);

        Assert.True(manager.CanTrack(key));
        Assert.Equal(ImeOpenStatus.Unknown, Assert.Single(manager.GetEntries()).Status);
    }

    [Fact]
    public void Disabling_Global_Memory_Stops_All_Tracking_And_Clears_Saved_States()
    {
        var store = new WindowStateStore();
        var manager = new WindowMemoryManager(store, globalEnabled: true);
        var key = new WindowKey(new IntPtr(101), 2001);
        manager.ObserveWindow(key, "Editor", "Editor", DateTimeOffset.UtcNow);
        store.Save(key, true);

        manager.SetGlobalEnabled(false);

        Assert.False(manager.IsGlobalEnabled);
        Assert.False(manager.CanTrack(key));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Prune_Removes_Closed_Windows_And_Their_Saved_State()
    {
        var store = new WindowStateStore();
        var manager = new WindowMemoryManager(store, globalEnabled: true);
        var alive = new WindowKey(new IntPtr(101), 2001);
        var dead = new WindowKey(new IntPtr(102), 2002);
        manager.ObserveWindow(alive, "Alive", "App", DateTimeOffset.UtcNow);
        manager.ObserveWindow(dead, "Dead", "App", DateTimeOffset.UtcNow);
        store.Save(alive, true);
        store.Save(dead, false);

        int removed = manager.Prune(key => key == alive);

        Assert.Equal(1, removed);
        Assert.Equal(alive, Assert.Single(manager.GetEntries()).Key);
        Assert.True(store.TryGet(alive, out _));
        Assert.False(store.TryGet(dead, out _));
    }

    [Fact]
    public void Persisted_Profile_Seeds_New_Runtime_Window_State()
    {
        var stateStore = new WindowStateStore();
        var persistence = new FakePersistenceStore
        {
            Entries =
            [
                new PersistedWindowMemoryEntry(
                    "Notepad",
                    "notes.txt - Notepad",
                    Enabled: true,
                    IsImeOpen: true,
                    DateTimeOffset.UtcNow)
            ]
        };
        var manager = new WindowMemoryManager(stateStore, globalEnabled: true);
        manager.ConfigurePersistence(enabled: true, persistence);
        var key = new WindowKey(new IntPtr(301), 3001);

        WindowMemoryObservationResult result = manager.ObserveWindow(
            key,
            "notes.txt - Notepad",
            "notepad",
            DateTimeOffset.UtcNow);

        Assert.True(result.HasPersistedState);
        Assert.True(stateStore.TryGet(key, out bool isOpen));
        Assert.True(isOpen);
        Assert.Equal(ImeOpenStatus.Open, Assert.Single(manager.GetEntries()).Status);
    }

    [Fact]
    public void Persisted_Disabled_Profile_Disables_Matching_Runtime_Window()
    {
        var persistence = new FakePersistenceStore
        {
            Entries =
            [
                new PersistedWindowMemoryEntry(
                    "Editor",
                    "Project",
                    Enabled: false,
                    IsImeOpen: false,
                    DateTimeOffset.UtcNow)
            ]
        };
        var manager = new WindowMemoryManager(new WindowStateStore(), globalEnabled: true);
        manager.ConfigurePersistence(enabled: true, persistence);
        var key = new WindowKey(new IntPtr(302), 3002);

        manager.ObserveWindow(key, "Project", "Editor", DateTimeOffset.UtcNow);

        Assert.False(manager.CanTrack(key));
        Assert.False(Assert.Single(manager.GetEntries()).Enabled);
    }

    [Fact]
    public void Status_And_PerWindow_Toggle_Are_Saved_When_Persistence_Is_Enabled()
    {
        var persistence = new FakePersistenceStore();
        var manager = new WindowMemoryManager(new WindowStateStore(), globalEnabled: true);
        manager.ConfigurePersistence(enabled: true, persistence);
        var key = new WindowKey(new IntPtr(303), 3003);
        manager.ObserveWindow(key, "Document", "Writer", DateTimeOffset.UtcNow);

        manager.UpdateStatus(key, ImeOpenStatus.Open);
        manager.SetWindowEnabled(key, false);

        PersistedWindowMemoryEntry saved = Assert.Single(persistence.Entries);
        Assert.Equal("Writer", saved.ProcessName);
        Assert.Equal("Document", saved.Title);
        Assert.False(saved.Enabled);
        Assert.True(saved.IsImeOpen);
        Assert.True(persistence.SaveCount >= 2);
    }

    [Fact]
    public void Changing_Persistence_Path_Copies_Profiles_Even_Without_Runtime_Windows()
    {
        var oldStore = new FakePersistenceStore("old.json")
        {
            Entries =
            [
                new PersistedWindowMemoryEntry(
                    "Writer",
                    "Document",
                    Enabled: true,
                    IsImeOpen: true,
                    DateTimeOffset.UtcNow)
            ]
        };
        var newStore = new FakePersistenceStore("new.json");
        var manager = new WindowMemoryManager(new WindowStateStore(), globalEnabled: true);
        manager.ConfigurePersistence(true, oldStore);

        manager.ConfigurePersistence(true, newStore);

        PersistedWindowMemoryEntry copied = Assert.Single(newStore.Entries);
        Assert.Equal("Writer", copied.ProcessName);
        Assert.True(copied.IsImeOpen);
    }

    [Fact]
    public void Enabling_Persistence_Applies_Existing_File_Profile_Instead_Of_Overwriting_It()
    {
        var manager = new WindowMemoryManager(new WindowStateStore(), globalEnabled: true);
        var key = new WindowKey(new IntPtr(304), 3004);
        manager.ObserveWindow(key, "Document", "Writer", DateTimeOffset.UtcNow);
        manager.UpdateStatus(key, ImeOpenStatus.Closed);
        var persistence = new FakePersistenceStore
        {
            Entries =
            [
                new PersistedWindowMemoryEntry(
                    "Writer",
                    "Document",
                    Enabled: false,
                    IsImeOpen: true,
                    DateTimeOffset.UtcNow.AddDays(-1))
            ]
        };

        manager.ConfigurePersistence(true, persistence);

        WindowMemoryEntry entry = Assert.Single(manager.GetEntries());
        Assert.False(entry.Enabled);
        Assert.Equal(ImeOpenStatus.Open, entry.Status);
        PersistedWindowMemoryEntry saved = Assert.Single(persistence.Entries);
        Assert.False(saved.Enabled);
        Assert.True(saved.IsImeOpen);
    }

    [Fact]
    public void Changing_Path_Keeps_Newer_Profile_From_Target_File()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var oldStore = new FakePersistenceStore("old.json")
        {
            Entries =
            [new PersistedWindowMemoryEntry("Writer", "Document", true, true, now.AddMinutes(-5))]
        };
        var newStore = new FakePersistenceStore("new.json")
        {
            Entries =
            [new PersistedWindowMemoryEntry("Writer", "Document", true, false, now)]
        };
        var manager = new WindowMemoryManager(new WindowStateStore(), globalEnabled: true);
        manager.ConfigurePersistence(true, oldStore);

        manager.ConfigurePersistence(true, newStore);

        Assert.False(Assert.Single(newStore.Entries).IsImeOpen);
    }

    [Fact]
    public void Window_Title_Change_Migrates_Persisted_Profile()
    {
        var persistence = new FakePersistenceStore();
        var manager = new WindowMemoryManager(new WindowStateStore(), globalEnabled: true);
        manager.ConfigurePersistence(true, persistence);
        var key = new WindowKey(new IntPtr(305), 3005);
        manager.ObserveWindow(key, "Untitled - Writer", "Writer", DateTimeOffset.UtcNow);
        manager.UpdateStatus(key, ImeOpenStatus.Open);

        manager.ObserveWindow(key, "notes.txt - Writer", "Writer", DateTimeOffset.UtcNow.AddMinutes(1));

        PersistedWindowMemoryEntry saved = Assert.Single(persistence.Entries);
        Assert.Equal("notes.txt - Writer", saved.Title);
        Assert.True(saved.IsImeOpen);
    }

    [Fact]
    public void Persistence_Write_Failure_Is_Exposed_To_Settings_Source()
    {
        var persistence = new FakePersistenceStore { FailSaves = true };
        var manager = new WindowMemoryManager(new WindowStateStore(), globalEnabled: true);
        manager.ConfigurePersistence(true, persistence);
        var key = new WindowKey(new IntPtr(306), 3006);

        manager.ObserveWindow(key, "Document", "Writer", DateTimeOffset.UtcNow);

        Assert.Equal("disk is read-only", manager.PersistenceError);
    }

    [Fact]
    public void Reconfiguring_Same_Path_Retries_A_Previous_Failed_Save()
    {
        var persistence = new FakePersistenceStore { FailSaves = true };
        var manager = new WindowMemoryManager(new WindowStateStore(), globalEnabled: true);
        manager.ConfigurePersistence(true, persistence);
        manager.ObserveWindow(new WindowKey(new IntPtr(307), 3007), "Document", "Writer", DateTimeOffset.UtcNow);
        persistence.FailSaves = false;

        manager.ConfigurePersistence(true, persistence);

        Assert.Null(manager.PersistenceError);
        Assert.Single(persistence.Entries);
    }

    [Fact]
    public void Title_Change_To_Existing_Profile_Applies_Profile_Without_Overwriting_It()
    {
        var persistence = new FakePersistenceStore
        {
            Entries =
            [new PersistedWindowMemoryEntry("Writer", "Saved.txt - Writer", true, true, DateTimeOffset.UtcNow)]
        };
        var stateStore = new WindowStateStore();
        var manager = new WindowMemoryManager(stateStore, globalEnabled: true);
        manager.ConfigurePersistence(true, persistence);
        var key = new WindowKey(new IntPtr(308), 3008);
        manager.ObserveWindow(key, "Untitled - Writer", "Writer", DateTimeOffset.UtcNow);
        manager.UpdateStatus(key, ImeOpenStatus.Closed);

        WindowMemoryObservationResult result = manager.ObserveWindow(
            key,
            "Saved.txt - Writer",
            "Writer",
            DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.True(result.HasPersistedState);
        Assert.True(stateStore.TryGet(key, out bool isOpen));
        Assert.True(isOpen);
        Assert.Contains(persistence.Entries, entry => entry.Title == "Saved.txt - Writer" && entry.IsImeOpen == true);
    }

    [Fact]
    public void Ambiguous_Identical_Window_Titles_Do_Not_Overwrite_Shared_Profile()
    {
        var persistence = new FakePersistenceStore();
        var manager = new WindowMemoryManager(new WindowStateStore(), globalEnabled: true);
        manager.ConfigurePersistence(true, persistence);
        var first = new WindowKey(new IntPtr(309), 3009);
        var second = new WindowKey(new IntPtr(310), 3010);
        manager.ObserveWindow(first, "Untitled - Writer", "Writer", DateTimeOffset.UtcNow);
        manager.UpdateStatus(first, ImeOpenStatus.Open);
        manager.ObserveWindow(second, "Untitled - Writer", "Writer", DateTimeOffset.UtcNow);
        int savesBeforeToggle = persistence.SaveCount;

        manager.SetWindowEnabled(second, false);

        Assert.Equal(savesBeforeToggle, persistence.SaveCount);
        Assert.True(Assert.Single(persistence.Entries).Enabled);
    }

    private sealed class FakePersistenceStore : IWindowMemoryPersistenceStore
    {
        public FakePersistenceStore(string storagePath = "memory.json")
        {
            StoragePath = storagePath;
        }

        public string StoragePath { get; }
        public IReadOnlyList<PersistedWindowMemoryEntry> Entries { get; set; } = [];
        public int SaveCount { get; private set; }
        public bool FailSaves { get; set; }

        public IReadOnlyList<PersistedWindowMemoryEntry> Load() => Entries;

        public bool TrySave(IReadOnlyCollection<PersistedWindowMemoryEntry> entries, out string? error)
        {
            if (FailSaves)
            {
                error = "disk is read-only";
                return false;
            }

            Entries = entries.ToArray();
            SaveCount++;
            error = null;
            return true;
        }
    }
}
