using ImeTool.Ime;
using ImeTool.State;

namespace ImeTool.Tests.State;

public sealed class WindowMemoryPersistenceStoreTests
{
    [Fact]
    public void Save_Then_Load_RoundTrips_Profiles_At_Custom_Path()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "custom-memory.json");
        var store = new WindowMemoryPersistenceStore(path);
        PersistedWindowMemoryEntry expected = new(
            "Notepad",
            "notes.txt - Notepad",
            Enabled: true,
            IsImeOpen: true,
            new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero));

        Assert.True(store.TrySave([expected], out string? error), error);
        IReadOnlyList<PersistedWindowMemoryEntry> actual = store.Load();

        Assert.Equal(path, store.StoragePath);
        Assert.Equal(expected, Assert.Single(actual));
    }

    [Fact]
    public void Load_Returns_Empty_For_Corrupt_File()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "window-memory.json");
        File.WriteAllText(path, "{ broken json");

        IReadOnlyList<PersistedWindowMemoryEntry> entries = new WindowMemoryPersistenceStore(path).Load();

        Assert.Empty(entries);
    }

    [Fact]
    public void ResolvePath_Uses_Default_For_Blank_And_Expands_Environment_Variables()
    {
        Assert.Equal(
            WindowMemoryPersistenceStore.DefaultPath,
            WindowMemoryPersistenceStore.ResolvePath(" "));

        string resolved = WindowMemoryPersistenceStore.ResolvePath("%TEMP%\\ImeTool\\memory.json");
        Assert.Equal(
            Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ImeTool", "memory.json")),
            resolved,
            ignoreCase: true);
    }

    [Fact]
    public void Persisted_State_Is_Available_To_A_New_Manager_Instance()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "memory.json");
        var firstStore = new WindowStateStore();
        var firstManager = new WindowMemoryManager(firstStore, globalEnabled: true);
        firstManager.ConfigurePersistence(true, new WindowMemoryPersistenceStore(path));
        var firstKey = new WindowKey(new IntPtr(501), 5001);
        firstManager.ObserveWindow(firstKey, "Draft - Writer", "Writer", DateTimeOffset.UtcNow);
        firstManager.UpdateStatus(firstKey, ImeOpenStatus.Open);

        var secondStore = new WindowStateStore();
        var secondManager = new WindowMemoryManager(secondStore, globalEnabled: true);
        secondManager.ConfigurePersistence(true, new WindowMemoryPersistenceStore(path));
        var secondKey = new WindowKey(new IntPtr(601), 6001);
        WindowMemoryObservationResult observation = secondManager.ObserveWindow(
            secondKey,
            "Draft - Writer",
            "writer",
            DateTimeOffset.UtcNow);

        Assert.True(observation.HasPersistedState);
        Assert.True(secondStore.TryGet(secondKey, out bool isOpen));
        Assert.True(isOpen);
    }

    [Fact]
    public void TrySave_Reports_Error_When_Storage_Path_Is_A_Directory()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new WindowMemoryPersistenceStore(directory);

        bool saved = store.TrySave([], out string? error);

        Assert.False(saved);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
