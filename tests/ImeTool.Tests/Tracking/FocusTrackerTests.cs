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

    private sealed class FakeImeService : IImeService
    {
        public Dictionary<IntPtr, ImeOpenStatus> StatusByHwnd { get; } = new();
        public List<(IntPtr Hwnd, bool IsOpen)> SetCalls { get; } = new();

        public ImeOpenStatus GetOpenStatus(IntPtr hwnd) => StatusByHwnd.TryGetValue(hwnd, out ImeOpenStatus status) ? status : ImeOpenStatus.Unknown;

        public bool SetOpenStatus(IntPtr hwnd, bool isOpen)
        {
            SetCalls.Add((hwnd, isOpen));
            StatusByHwnd[hwnd] = ImeOpenStatusExtensions.FromBool(isOpen);
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
