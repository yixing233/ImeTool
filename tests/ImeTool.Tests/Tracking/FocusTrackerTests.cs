using ImeTool.Ime;
using ImeTool.State;
using ImeTool.Tracking;

namespace ImeTool.Tests.Tracking;

public sealed class FocusTrackerTests
{
    [Fact]
    public void Switching_Back_To_Window_Restores_Saved_Ime_State()
    {
        var ime = new FakeImeService();
        var windows = new FakeWindowInfoService();
        var store = new WindowStateStore();
        var tracker = new FocusTracker(ime, store, windows);

        IntPtr focusA = new(11);
        IntPtr focusB = new(22);
        windows.Map(focusA, new WindowKey(new IntPtr(1), 1001));
        windows.Map(focusB, new WindowKey(new IntPtr(2), 1002));

        ime.StatusByHwnd[focusA] = ImeOpenStatus.Open;
        tracker.HandleFocusChanged(focusA);
        Assert.Equal(ImeOpenStatus.Open, tracker.UpdateCurrentImeState(focusA));

        ime.StatusByHwnd[focusB] = ImeOpenStatus.Closed;
        tracker.HandleFocusChanged(focusB);
        Assert.Equal(ImeOpenStatus.Closed, tracker.UpdateCurrentImeState(focusB));

        tracker.HandleFocusChanged(focusA);

        Assert.Contains((focusA, true), ime.SetCalls);
    }

    [Fact]
    public void Unknown_Status_Is_Not_Saved()
    {
        var ime = new FakeImeService();
        var windows = new FakeWindowInfoService();
        var store = new WindowStateStore();
        var tracker = new FocusTracker(ime, store, windows);
        IntPtr focus = new(11);
        var key = new WindowKey(new IntPtr(1), 1001);
        windows.Map(focus, key);
        ime.StatusByHwnd[focus] = ImeOpenStatus.Unknown;

        tracker.HandleFocusChanged(focus);
        tracker.UpdateCurrentImeState(focus);

        Assert.False(store.TryGet(key, out _));
    }

    [Fact]
    public void Same_Window_Focus_Change_Does_Not_Restore_Repeatedly()
    {
        var ime = new FakeImeService();
        var windows = new FakeWindowInfoService();
        var store = new WindowStateStore();
        var tracker = new FocusTracker(ime, store, windows);
        IntPtr focusA1 = new(11);
        IntPtr focusA2 = new(12);
        var key = new WindowKey(new IntPtr(1), 1001);
        windows.Map(focusA1, key);
        windows.Map(focusA2, key);
        store.Save(key, true);

        tracker.HandleFocusChanged(focusA1);
        tracker.HandleFocusChanged(focusA2);

        Assert.Single(ime.SetCalls);
        Assert.Equal((focusA1, true), ime.SetCalls[0]);
    }

    [Fact]
    public void Restore_Policy_Can_Skip_A_Saved_Window()
    {
        var ime = new FakeImeService();
        var windows = new FakeWindowInfoService();
        var store = new WindowStateStore();
        var blocked = new WindowKey(new IntPtr(1), 1001);
        var tracker = new FocusTracker(ime, store, windows, key => key != blocked);
        IntPtr focus = new(11);
        windows.Map(focus, blocked);
        store.Save(blocked, true);

        tracker.HandleFocusChanged(focus);

        Assert.Empty(ime.SetCalls);
        Assert.Equal(blocked, tracker.CurrentWindow);
    }

    [Fact]
    public void Failed_Root_Window_Restore_Retries_On_Text_Focus_Child()
    {
        var ime = new FakeImeService();
        var windows = new FakeWindowInfoService();
        var store = new WindowStateStore();
        var tracker = new FocusTracker(ime, store, windows);
        IntPtr root = new(20);
        IntPtr child = new(21);
        var key = new WindowKey(root, 1002);
        windows.Map(root, key);
        windows.Map(child, key);
        store.Save(key, true);
        ime.SetResultsByHwnd[root] = false;
        ime.SetResultsByHwnd[child] = true;

        tracker.HandleFocusChanged(root);
        tracker.HandleFocusChanged(child);

        Assert.Equal([(root, true), (child, true)], ime.SetCalls);
    }

    [Fact]
    public void Pending_Restore_Is_Not_Overwritten_By_PreRestore_Status()
    {
        var ime = new FakeImeService();
        var windows = new FakeWindowInfoService();
        var store = new WindowStateStore();
        var tracker = new FocusTracker(ime, store, windows);
        IntPtr root = new(20);
        IntPtr child = new(21);
        var key = new WindowKey(root, 1002);
        windows.Map(root, key);
        windows.Map(child, key);
        store.Save(key, true);
        ime.SetResultsByHwnd[root] = false;
        ime.SetResultsByHwnd[child] = false;
        ime.StatusByHwnd[child] = ImeOpenStatus.Closed;

        tracker.HandleFocusChanged(root);
        tracker.UpdateCurrentImeState(child);

        Assert.True(store.TryGet(key, out bool savedIsOpen));
        Assert.True(savedIsOpen);
    }

    [Fact]
    public void Disabled_Tracking_Policy_Prevents_Save_And_Restore()
    {
        var ime = new FakeImeService();
        var windows = new FakeWindowInfoService();
        var store = new WindowStateStore();
        bool enabled = false;
        var tracker = new FocusTracker(ime, store, windows, _ => enabled);
        IntPtr focus = new(11);
        var key = new WindowKey(new IntPtr(1), 1001);
        windows.Map(focus, key);
        store.Save(key, true);
        ime.StatusByHwnd[focus] = ImeOpenStatus.Closed;

        tracker.HandleFocusChanged(focus);
        tracker.UpdateCurrentImeState(focus);

        Assert.Empty(ime.SetCalls);
        Assert.True(store.TryGet(key, out bool savedState));
        Assert.True(savedState);
    }

    [Fact]
    public void Disabling_Current_Window_Cancels_Pending_Restore()
    {
        var ime = new FakeImeService();
        var windows = new FakeWindowInfoService();
        var store = new WindowStateStore();
        bool enabled = true;
        var tracker = new FocusTracker(ime, store, windows, _ => enabled);
        IntPtr root = new(20);
        IntPtr child = new(21);
        var key = new WindowKey(root, 1002);
        windows.Map(root, key);
        windows.Map(child, key);
        store.Save(key, true);
        ime.SetResultsByHwnd[root] = false;

        tracker.HandleFocusChanged(root);
        enabled = false;
        tracker.HandleFocusChanged(child);

        Assert.Equal([(root, true)], ime.SetCalls);
    }

    [Fact]
    public void Successful_Restore_Is_Not_Immediately_Overwritten_By_Delayed_Status_Read()
    {
        var ime = new FakeImeService();
        var windows = new FakeWindowInfoService();
        var store = new WindowStateStore();
        var tracker = new FocusTracker(ime, store, windows);
        IntPtr focus = new(11);
        var key = new WindowKey(new IntPtr(1), 1001);
        windows.Map(focus, key);
        store.Save(key, true);
        ime.StatusByHwnd[focus] = ImeOpenStatus.Closed;
        ime.KeepStatusAfterSet.Add(focus);

        tracker.HandleFocusChanged(focus);
        tracker.UpdateCurrentImeState(focus);

        Assert.True(store.TryGet(key, out bool savedState));
        Assert.True(savedState);
    }

    [Fact]
    public void Failed_Child_Restore_Retries_After_Cooldown()
    {
        var ime = new FakeImeService();
        var windows = new FakeWindowInfoService();
        var store = new WindowStateStore();
        DateTimeOffset now = new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var tracker = new FocusTracker(ime, store, windows, nowProvider: () => now);
        IntPtr root = new(20);
        IntPtr child = new(21);
        var key = new WindowKey(root, 1002);
        windows.Map(root, key);
        windows.Map(child, key);
        store.Save(key, true);
        ime.SetResultsByHwnd[root] = false;
        ime.SetResultsByHwnd[child] = false;

        tracker.HandleFocusChanged(root);
        tracker.HandleFocusChanged(child);
        now = now.AddMilliseconds(250);
        ime.SetResultsByHwnd[child] = true;
        tracker.UpdateCurrentImeState(child);

        Assert.Equal([(root, true), (child, true), (child, true)], ime.SetCalls);
    }

    [Fact]
    public void Restore_Gives_Up_After_Three_Attempts_And_Records_Actual_State()
    {
        var ime = new FakeImeService();
        var windows = new FakeWindowInfoService();
        var store = new WindowStateStore();
        DateTimeOffset now = new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var tracker = new FocusTracker(ime, store, windows, nowProvider: () => now);
        IntPtr root = new(20);
        IntPtr child = new(21);
        var key = new WindowKey(root, 1002);
        windows.Map(root, key);
        windows.Map(child, key);
        store.Save(key, true);
        ime.StatusByHwnd[child] = ImeOpenStatus.Closed;
        ime.SetResultsByHwnd[root] = false;
        ime.SetResultsByHwnd[child] = false;

        tracker.HandleFocusChanged(root);
        tracker.HandleFocusChanged(child);
        now = now.AddMilliseconds(250);
        tracker.UpdateCurrentImeState(child);

        Assert.Equal(3, ime.SetCalls.Count);
        Assert.True(store.TryGet(key, out bool savedState));
        Assert.False(savedState);
    }

    private sealed class FakeImeService : IImeService
    {
        public Dictionary<IntPtr, ImeOpenStatus> StatusByHwnd { get; } = new();
        public Dictionary<IntPtr, bool> SetResultsByHwnd { get; } = new();
        public HashSet<IntPtr> KeepStatusAfterSet { get; } = [];
        public List<(IntPtr Hwnd, bool IsOpen)> SetCalls { get; } = new();

        public ImeOpenStatus GetOpenStatus(IntPtr hwnd) => StatusByHwnd.TryGetValue(hwnd, out ImeOpenStatus status) ? status : ImeOpenStatus.Unknown;

        public bool SetOpenStatus(IntPtr hwnd, bool isOpen)
        {
            SetCalls.Add((hwnd, isOpen));
            if (SetResultsByHwnd.TryGetValue(hwnd, out bool result) && !result)
            {
                return false;
            }

            if (!KeepStatusAfterSet.Contains(hwnd))
            {
                StatusByHwnd[hwnd] = ImeOpenStatusExtensions.FromBool(isOpen);
            }

            return true;
        }
    }

    private sealed class FakeWindowInfoService : IWindowInfoService
    {
        private readonly Dictionary<IntPtr, WindowKey> _map = new();

        public void Map(IntPtr focusHwnd, WindowKey key) => _map[focusHwnd] = key;

        public bool TryGetWindowKey(IntPtr hwnd, out WindowKey key) => _map.TryGetValue(hwnd, out key);

        public IntPtr GetRootWindow(IntPtr hwnd) => TryGetWindowKey(hwnd, out WindowKey key) ? key.Hwnd : IntPtr.Zero;

        public bool IsWindow(IntPtr hwnd) => hwnd != IntPtr.Zero;

        public bool IsWindowVisible(IntPtr hwnd) => true;

        public bool IsIconic(IntPtr hwnd) => false;
    }
}
