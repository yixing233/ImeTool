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
    public void Same_Window_Unvalidated_Focus_Changes_Do_Not_Reapply_Pending_Restore()
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

        Assert.Equal([(focusA1, true)], ime.SetCalls);
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
    public void Failed_Root_Window_Restore_Retries_On_Validated_Text_Focus_Child()
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
        tracker.UpdateCurrentImeState(child);

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

        Assert.Equal([(root, true), (child, true)], ime.SetCalls);
    }

    [Fact]
    public void Restore_Gives_Up_After_Max_Attempts_Without_Overwriting_Saved_State()
    {
        var ime = new FakeImeService { KeepInputModeAfterToggle = true };
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
        ime.ModeByHwnd[child] = TextInputMode.English;
        ime.SetResultsByHwnd[root] = false;
        ime.SetResultsByHwnd[child] = false;

        tracker.HandleFocusChanged(root);
        tracker.HandleFocusChanged(child);
        now = now.AddMilliseconds(250);
        tracker.UpdateCurrentImeState(child);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            now = now.AddMilliseconds(200);
            tracker.UpdateCurrentImeState(child);
        }

        Assert.Equal(2, ime.SetCalls.Count);
        Assert.Single(ime.ToggleCalls);
        Assert.True(store.TryGet(key, out bool savedState));
        Assert.True(savedState);
    }

    [Fact]
    public void Persisted_State_Can_Be_Requested_After_Current_Window_Was_Observed()
    {
        var ime = new FakeImeService();
        var windows = new FakeWindowInfoService();
        var store = new WindowStateStore();
        var tracker = new FocusTracker(ime, store, windows);
        IntPtr focus = new(41);
        var key = new WindowKey(new IntPtr(4), 1004);
        windows.Map(focus, key);
        tracker.HandleFocusChanged(focus);
        store.Save(key, true);

        tracker.RequestRestoreCurrentWindowState(focus);

        Assert.Contains((focus, true), ime.SetCalls);
    }

    [Fact]
    public void Remembered_State_Uses_Actual_Input_Mode_When_Open_Status_Stays_Open()
    {
        var ime = new FakeImeService();
        var windows = new FakeWindowInfoService();
        var store = new WindowStateStore();
        var tracker = new FocusTracker(ime, store, windows);
        IntPtr focus = new(51);
        var key = new WindowKey(new IntPtr(5), 1005);
        windows.Map(focus, key);
        ime.StatusByHwnd[focus] = ImeOpenStatus.Open;
        ime.ModeByHwnd[focus] = TextInputMode.English;

        tracker.HandleFocusChanged(focus);
        tracker.UpdateCurrentImeState(focus);

        Assert.True(store.TryGet(key, out bool rememberedChinese));
        Assert.False(rememberedChinese);
    }

    [Fact]
    public void Validated_Mode_Mismatch_Uses_Shift_Fallback_After_Grace_Period()
    {
        var ime = new FakeImeService();
        var windows = new FakeWindowInfoService();
        var store = new WindowStateStore();
        DateTimeOffset now = new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var tracker = new FocusTracker(ime, store, windows, nowProvider: () => now);
        IntPtr focus = new(61);
        var key = new WindowKey(new IntPtr(6), 1006);
        windows.Map(focus, key);
        store.Save(key, true);
        ime.StatusByHwnd[focus] = ImeOpenStatus.Open;
        ime.ModeByHwnd[focus] = TextInputMode.English;
        ime.KeepInputModeAfterSet.Add(focus);
        var applied = new List<(WindowKey Key, TextInputMode Mode)>();
        tracker.FallbackInputModeApplied += (window, mode) => applied.Add((window, mode));

        tracker.HandleFocusChanged(focus);
        now = now.AddMilliseconds(250);
        tracker.UpdateCurrentImeState(focus);
        now = now.AddMilliseconds(200);
        tracker.UpdateCurrentImeState(focus);

        Assert.Equal([focus], ime.ToggleCalls);
        Assert.Equal(TextInputMode.Chinese, ime.ModeByHwnd[focus]);
        Assert.Equal([(key, TextInputMode.Chinese)], applied);
    }

    [Fact]
    public void Unverified_Shift_Fallback_Does_Not_Report_False_Success()
    {
        var ime = new FakeImeService { KeepInputModeAfterToggle = true };
        var windows = new FakeWindowInfoService();
        var store = new WindowStateStore();
        DateTimeOffset now = new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var tracker = new FocusTracker(ime, store, windows, nowProvider: () => now);
        IntPtr focus = new(62);
        var key = new WindowKey(new IntPtr(6), 1006);
        windows.Map(focus, key);
        store.Save(key, true);
        ime.StatusByHwnd[focus] = ImeOpenStatus.Open;
        ime.ModeByHwnd[focus] = TextInputMode.English;
        ime.KeepInputModeAfterSet.Add(focus);
        var applied = new List<(WindowKey Key, TextInputMode Mode)>();
        tracker.FallbackInputModeApplied += (window, mode) => applied.Add((window, mode));

        tracker.HandleFocusChanged(focus);
        now = now.AddMilliseconds(250);
        tracker.UpdateCurrentImeState(focus);
        now = now.AddMilliseconds(200);
        tracker.UpdateCurrentImeState(focus);

        Assert.Single(ime.ToggleCalls);
        Assert.Empty(applied);
        Assert.Equal(TextInputMode.English, ime.ModeByHwnd[focus]);
    }

    [Fact]
    public void Switching_Away_After_Unverified_Fallback_Prevents_Second_Shift_On_Return()
    {
        var ime = new FakeImeService { KeepInputModeAfterToggle = true };
        var windows = new FakeWindowInfoService();
        var store = new WindowStateStore();
        DateTimeOffset now = new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var tracker = new FocusTracker(ime, store, windows, nowProvider: () => now);
        IntPtr focusA = new(63);
        IntPtr focusB = new(64);
        var keyA = new WindowKey(new IntPtr(6), 1006);
        var keyB = new WindowKey(new IntPtr(7), 1007);
        windows.Map(focusA, keyA);
        windows.Map(focusB, keyB);
        store.Save(keyA, true);
        store.Save(keyB, false);
        ime.ModeByHwnd[focusA] = TextInputMode.English;
        ime.ModeByHwnd[focusB] = TextInputMode.English;
        ime.KeepInputModeAfterSet.Add(focusA);

        tracker.HandleFocusChanged(focusA);
        now = now.AddMilliseconds(250);
        tracker.UpdateCurrentImeState(focusA);
        tracker.HandleFocusChanged(focusB);
        tracker.UpdateCurrentImeState(focusB);
        tracker.HandleFocusChanged(focusA);
        now = now.AddMilliseconds(250);
        tracker.UpdateCurrentImeState(focusA);

        Assert.Single(ime.ToggleCalls);
    }

    [Fact]
    public void Unknown_Mode_With_Open_Status_Does_Not_Overwrite_Window_Memory()
    {
        var ime = new FakeImeService();
        var windows = new FakeWindowInfoService();
        var store = new WindowStateStore();
        var tracker = new FocusTracker(ime, store, windows);
        IntPtr focus = new(71);
        var key = new WindowKey(new IntPtr(7), 1007);
        windows.Map(focus, key);
        ime.StatusByHwnd[focus] = ImeOpenStatus.Open;
        ime.ModeByHwnd[focus] = TextInputMode.Unknown;

        tracker.HandleFocusChanged(focus);
        tracker.UpdateCurrentImeState(focus);

        Assert.False(store.TryGet(key, out _));
    }

    private sealed class FakeImeService : IImeService
    {
        public Dictionary<IntPtr, ImeOpenStatus> StatusByHwnd { get; } = new();
        public Dictionary<IntPtr, bool> SetResultsByHwnd { get; } = new();
        public HashSet<IntPtr> KeepStatusAfterSet { get; } = [];
        public Dictionary<IntPtr, TextInputMode> ModeByHwnd { get; } = new();
        public HashSet<IntPtr> KeepInputModeAfterSet { get; } = [];
        public List<(IntPtr Hwnd, bool IsOpen)> SetCalls { get; } = new();
        public List<IntPtr> ToggleCalls { get; } = new();
        public bool KeepInputModeAfterToggle { get; set; }

        public ImeOpenStatus GetOpenStatus(IntPtr hwnd) => StatusByHwnd.TryGetValue(hwnd, out ImeOpenStatus status) ? status : ImeOpenStatus.Unknown;

        public TextInputMode GetInputMode(IntPtr hwnd) => ModeByHwnd.TryGetValue(hwnd, out TextInputMode mode)
            ? mode
            : TextInputModeResolver.Resolve(GetOpenStatus(hwnd), conversionModeKnown: false, conversionMode: 0);

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

            if (!KeepInputModeAfterSet.Contains(hwnd))
            {
                ModeByHwnd[hwnd] = isOpen ? TextInputMode.Chinese : TextInputMode.English;
            }

            return true;
        }

        public bool ToggleInputMode(IntPtr hwnd)
        {
            ToggleCalls.Add(hwnd);
            TextInputMode current = GetInputMode(hwnd);
            if (!KeepInputModeAfterToggle)
            {
                ModeByHwnd[hwnd] = current == TextInputMode.Chinese
                    ? TextInputMode.English
                    : TextInputMode.Chinese;
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
