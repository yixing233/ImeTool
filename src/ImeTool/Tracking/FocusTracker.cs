using System.Diagnostics;
using ImeTool.Ime;
using ImeTool.State;

namespace ImeTool.Tracking;

public sealed class FocusTracker
{
    private readonly IImeService _imeService;
    private readonly WindowStateStore _stateStore;
    private readonly IWindowInfoService _windowInfo;
    private readonly Func<WindowKey, bool> _canRestoreState;

    private WindowKey? _currentWindow;
    private IntPtr _currentFocusHwnd;

    public FocusTracker(
        IImeService imeService,
        WindowStateStore stateStore,
        IWindowInfoService windowInfo,
        Func<WindowKey, bool>? canRestoreState = null)
    {
        _imeService = imeService;
        _stateStore = stateStore;
        _windowInfo = windowInfo;
        _canRestoreState = canRestoreState ?? (_ => true);
    }

    public WindowKey? CurrentWindow => _currentWindow;

    public void HandleFocusChanged(IntPtr focusedHwnd)
    {
        if (!_windowInfo.TryGetWindowKey(focusedHwnd, out WindowKey newWindow))
        {
            return;
        }

        if (_currentWindow == newWindow)
        {
            _currentFocusHwnd = focusedHwnd;
            return;
        }

        SaveCurrentWindowState();

        _currentWindow = newWindow;
        _currentFocusHwnd = focusedHwnd;

        if (_canRestoreState(newWindow) && _stateStore.TryGet(newWindow, out bool savedIsOpen))
        {
            bool restored = _imeService.SetOpenStatus(focusedHwnd, savedIsOpen);
            if (!restored)
            {
                Debug.WriteLine($"Failed to restore IME state {savedIsOpen} to window {newWindow} focus 0x{focusedHwnd.ToInt64():X}.");
            }
        }
    }

    public ImeOpenStatus UpdateCurrentImeState(IntPtr focusedHwnd)
    {
        if (!_windowInfo.TryGetWindowKey(focusedHwnd, out WindowKey key))
        {
            return ImeOpenStatus.Unknown;
        }

        if (_currentWindow != key)
        {
            HandleFocusChanged(focusedHwnd);
        }
        else
        {
            _currentFocusHwnd = focusedHwnd;
        }

        ImeOpenStatus status = _imeService.GetOpenStatus(focusedHwnd);
        bool? isOpen = status.ToNullableBool();
        if (isOpen.HasValue)
        {
            _stateStore.Save(key, isOpen.Value);
        }

        return status;
    }

    public void SaveCurrentWindowState()
    {
        if (!_currentWindow.HasValue || _currentFocusHwnd == IntPtr.Zero)
        {
            return;
        }

        ImeOpenStatus status = _imeService.GetOpenStatus(_currentFocusHwnd);
        bool? isOpen = status.ToNullableBool();
        if (isOpen.HasValue)
        {
            _stateStore.Save(_currentWindow.Value, isOpen.Value);
        }
    }
}
