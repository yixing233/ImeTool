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
}
