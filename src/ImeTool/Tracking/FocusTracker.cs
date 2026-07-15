using System.Diagnostics;
using ImeTool.Diagnostics;
using ImeTool.Ime;
using ImeTool.State;

namespace ImeTool.Tracking;

public sealed class FocusTracker
{
    private const int MaxRestoreAttempts = 3;
    private static readonly TimeSpan RestoreRetryDelay = TimeSpan.FromMilliseconds(200);

    private readonly IImeService _imeService;
    private readonly WindowStateStore _stateStore;
    private readonly IWindowInfoService _windowInfo;
    private readonly Func<WindowKey, bool> _canTrackState;
    private readonly Func<DateTimeOffset> _nowProvider;

    private WindowKey? _currentWindow;
    private IntPtr _currentFocusHwnd;
    private bool? _pendingRestoreState;
    private IntPtr _lastRestoreAttemptHwnd;
    private int _restoreAttemptCount;
    private DateTimeOffset _nextRestoreAttemptAt;
    private bool _skipNextStateSave;

    public FocusTracker(
        IImeService imeService,
        WindowStateStore stateStore,
        IWindowInfoService windowInfo,
        Func<WindowKey, bool>? canTrackState = null,
        Func<DateTimeOffset>? nowProvider = null)
    {
        _imeService = imeService;
        _stateStore = stateStore;
        _windowInfo = windowInfo;
        _canTrackState = canTrackState ?? (_ => true);
        _nowProvider = nowProvider ?? (() => DateTimeOffset.UtcNow);
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
            if (!_canTrackState(newWindow))
            {
                ClearPendingRestore();
                _skipNextStateSave = false;
                return;
            }

            TryRestorePendingState(focusedHwnd);
            return;
        }

        SaveCurrentWindowState();

        _currentWindow = newWindow;
        _currentFocusHwnd = focusedHwnd;
        ClearPendingRestore();
        _skipNextStateSave = false;

        if (_canTrackState(newWindow) && _stateStore.TryGet(newWindow, out bool savedIsOpen))
        {
            _pendingRestoreState = savedIsOpen;
            TryRestorePendingState(focusedHwnd);
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
            if (_canTrackState(key))
            {
                TryRestorePendingState(focusedHwnd);
            }
            else
            {
                ClearPendingRestore();
                _skipNextStateSave = false;
            }
        }

        ImeOpenStatus status = _imeService.GetOpenStatus(focusedHwnd);
        bool? isOpen = status.ToNullableBool();
        if (isOpen.HasValue &&
            !_pendingRestoreState.HasValue &&
            !_skipNextStateSave &&
            _canTrackState(key))
        {
            _stateStore.Save(key, isOpen.Value);
        }

        _skipNextStateSave = false;

        return status;
    }

    public void SaveCurrentWindowState()
    {
        if (!_currentWindow.HasValue ||
            _currentFocusHwnd == IntPtr.Zero ||
            _pendingRestoreState.HasValue ||
            _skipNextStateSave ||
            !_canTrackState(_currentWindow.Value))
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

    private void TryRestorePendingState(IntPtr focusedHwnd)
    {
        if (!_pendingRestoreState.HasValue ||
            focusedHwnd == IntPtr.Zero)
        {
            return;
        }

        DateTimeOffset now = _nowProvider();
        if (focusedHwnd == _lastRestoreAttemptHwnd && now < _nextRestoreAttemptAt)
        {
            return;
        }

        _lastRestoreAttemptHwnd = focusedHwnd;
        _restoreAttemptCount++;
        _nextRestoreAttemptAt = now + RestoreRetryDelay;
        bool savedIsOpen = _pendingRestoreState.Value;
        bool restored = _imeService.SetOpenStatus(focusedHwnd, savedIsOpen);
        if (restored)
        {
            DiagnosticsLog.Write(
                $"IME state restored: window={_currentWindow}, " +
                $"focus=0x{focusedHwnd.ToInt64():X}, isOpen={savedIsOpen}.");
            _pendingRestoreState = null;
            _skipNextStateSave = true;
            return;
        }

        if (_restoreAttemptCount >= MaxRestoreAttempts)
        {
            DiagnosticsLog.Write(
                $"IME state restore abandoned after {_restoreAttemptCount} attempts: " +
                $"window={_currentWindow}, focus=0x{focusedHwnd.ToInt64():X}, isOpen={savedIsOpen}.");
            ClearPendingRestore();
            return;
        }

        DiagnosticsLog.Write(
            $"IME state restore deferred: window={_currentWindow}, " +
            $"focus=0x{focusedHwnd.ToInt64():X}, isOpen={savedIsOpen}.");
        Debug.WriteLine(
            $"Failed to restore IME state {savedIsOpen} to window {_currentWindow} " +
            $"focus 0x{focusedHwnd.ToInt64():X}; waiting for a new focus target.");
    }

    private void ClearPendingRestore()
    {
        _pendingRestoreState = null;
        _lastRestoreAttemptHwnd = IntPtr.Zero;
        _restoreAttemptCount = 0;
        _nextRestoreAttemptAt = DateTimeOffset.MinValue;
    }
}
