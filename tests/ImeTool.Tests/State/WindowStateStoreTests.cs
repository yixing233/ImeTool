using ImeTool.State;

namespace ImeTool.Tests.State;

public sealed class WindowStateStoreTests
{
    [Fact]
    public void Save_And_TryGet_Returns_State()
    {
        var store = new WindowStateStore();
        var key = new WindowKey(new IntPtr(100), 42);

        store.Save(key, isImeOpen: true);

        Assert.True(store.TryGet(key, out bool isOpen));
        Assert.True(isOpen);
    }

    [Fact]
    public void Save_Overwrites_Existing_State()
    {
        var store = new WindowStateStore();
        var key = new WindowKey(new IntPtr(100), 42);

        store.Save(key, isImeOpen: true);
        store.Save(key, isImeOpen: false);

        Assert.True(store.TryGet(key, out bool isOpen));
        Assert.False(isOpen);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Different_Windows_Are_Independent()
    {
        var store = new WindowStateStore();
        var first = new WindowKey(new IntPtr(100), 42);
        var second = new WindowKey(new IntPtr(200), 42);

        store.Save(first, isImeOpen: true);
        store.Save(second, isImeOpen: false);

        Assert.True(store.TryGet(first, out bool firstOpen));
        Assert.True(store.TryGet(second, out bool secondOpen));
        Assert.True(firstOpen);
        Assert.False(secondOpen);
    }

    [Fact]
    public void Prune_Removes_Invalid_Windows()
    {
        var store = new WindowStateStore();
        var alive = new WindowKey(new IntPtr(100), 42);
        var dead = new WindowKey(new IntPtr(200), 42);
        store.Save(alive, true);
        store.Save(dead, false);

        int removed = store.Prune(hwnd => hwnd == alive.Hwnd);

        Assert.Equal(1, removed);
        Assert.True(store.TryGet(alive, out _));
        Assert.False(store.TryGet(dead, out _));
    }
}
