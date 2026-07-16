using System.Diagnostics;
using ImeTool.Diagnostics;
using ImeTool.Ime;
using ImeTool.State;

namespace ImeTool.Tracking;

public sealed class FocusTracker
{
    private const int MaxRestoreAttempts = 5;
    private static readonly TimeSpan RestoreRetryDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan NativeRestoreGracePeriod = TimeSpan.FromMilliseconds(150);

    private readonly IImeService _imeService;
    private readonly WindowStateStore _stateStore;
    private readonly IWindowInfoService _windowInfo;
    private readonly Func<WindowKey, bool> _canTrackState;
    private readonly Func<WindowKey, bool> _canRestoreState;
    private readonly Func<DateTimeOffset> _nowProvider;

    private WindowKey? _currentWindow;
    private IntPtr _currentFocusHwnd;
    private bool? _pendingRestoreState;
    private IntPtr _lastRestoreAttemptHwnd;
    private int _restoreAttemptCount;
    private DateTimeOffset _nextRestoreAttemptAt;
    private DateTimeOffset _restoreStartedAt;
    private bool _fallbackToggleAttempted;
    private bool _skipNextStateSave;
    private bool _suppressStateSaveUntilWindowChange;
    private WindowKey? _lastObservedModeWindow;
    private TextInputMode _lastObservedMode;
    private readonly HashSet<WindowKey> _unverifiedFallbackWindows = [];

    public FocusTracker(
        IImeService imeService,
        WindowStateStore stateStore,
        IWindowInfoService windowInfo,
        Func<WindowKey, bool>? canTrackState = null,
        Func<WindowKey, bool>? canRestoreState = null,
        Func<DateTimeOffset>? nowProvider = null)
    {
        _imeService = imeService;
        _stateStore = stateStore;
        _windowInfo = windowInfo;
        _canTrackState = canTrackState ?? (_ => true);
        _canRestoreState = canRestoreState ?? _canTrackState;
        _nowProvider = nowProvider ?? (() => DateTimeOffset.UtcNow);
    }

    public WindowKey? CurrentWindow => _currentWindow;

    public event Action<WindowKey, TextInputMode>? FallbackInputModeApplied;

    public void RecordCurrentInputMode(
        WindowKey key,
        TextInputMode mode,
        bool verifiedInputMode = true)
    {
        if (mode == TextInputMode.Unknown ||
            _currentWindow != key ||
            _pendingRestoreState.HasValue ||
            _skipNextStateSave ||
            _suppressStateSaveUntilWindowChange ||
            !_canTrackState(key))
        {
            return;
        }

        _lastObservedModeWindow = key;
        _lastObservedMode = mode;
        _stateStore.Save(key, mode == TextInputMode.Chinese);
        if (verifiedInputMode)
        {
            _unverifiedFallbackWindows.Remove(key);
        }
    }

    public void RequestRestoreCurrentWindowState(IntPtr focusedHwnd, bool validatedInputTarget = false)
    {
        if (!_windowInfo.TryGetWindowKey(focusedHwnd, out WindowKey key) ||
            _currentWindow != key ||
            !_canRestoreState(key) ||
            _pendingRestoreState.HasValue ||
            !_stateStore.TryGet(key, out bool savedIsOpen))
        {
            return;
        }

        _currentFocusHwnd = focusedHwnd;
        BeginPendingRestore(savedIsOpen);
        _suppressStateSaveUntilWindowChange = false;
        TryRestorePendingState(focusedHwnd, validatedInputTarget);
    }

    public void HandleFocusChanged(IntPtr focusedHwnd)
    {
        if (!_windowInfo.TryGetWindowKey(focusedHwnd, out WindowKey newWindow))
        {
            return;
        }

        if (_currentWindow == newWindow)
        {
            _currentFocusHwnd = focusedHwnd;
            if (!_canRestoreState(newWindow))
            {
                PreserveUnverifiedFallbackForCurrentWindow();
                ClearPendingRestore();
                _skipNextStateSave = false;
                _suppressStateSaveUntilWindowChange = false;
                return;
            }

            TryRestorePendingState(focusedHwnd, canConfirmInputMode: false);
            return;
        }

        PreserveUnverifiedFallbackForCurrentWindow();
        SaveCurrentWindowState();

        _currentWindow = newWindow;
        _currentFocusHwnd = focusedHwnd;
        ClearPendingRestore();
        _skipNextStateSave = false;
        _suppressStateSaveUntilWindowChange = false;
        _lastObservedModeWindow = null;
        _lastObservedMode = TextInputMode.Unknown;

        if (_canRestoreState(newWindow) && _stateStore.TryGet(newWindow, out bool savedIsOpen))
        {
            BeginPendingRestore(savedIsOpen);
            TryRestorePendingState(focusedHwnd, canConfirmInputMode: false);
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
            if (_canRestoreState(key))
            {
                TryRestorePendingState(focusedHwnd, canConfirmInputMode: true);
            }
            else
            {
                PreserveUnverifiedFallbackForCurrentWindow();
                ClearPendingRestore();
                _skipNextStateSave = false;
                _suppressStateSaveUntilWindowChange = false;
            }
        }

        ImeOpenStatus status = _imeService.GetOpenStatus(focusedHwnd);
        bool? rememberedChinese = ReadRememberedChineseState(focusedHwnd, status);
        if (rememberedChinese.HasValue &&
            !_pendingRestoreState.HasValue &&
            !_skipNextStateSave &&
            !_suppressStateSaveUntilWindowChange &&
            _canTrackState(key))
        {
            _stateStore.Save(key, rememberedChinese.Value);
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
            _suppressStateSaveUntilWindowChange ||
            !_canTrackState(_currentWindow.Value))
        {
            return;
        }

        bool? rememberedChinese = _lastObservedModeWindow == _currentWindow &&
                                  _lastObservedMode != TextInputMode.Unknown
            ? _lastObservedMode == TextInputMode.Chinese
            : ReadRememberedChineseState(
                _currentFocusHwnd,
                _imeService.GetOpenStatus(_currentFocusHwnd));
        if (rememberedChinese.HasValue)
        {
            _stateStore.Save(_currentWindow.Value, rememberedChinese.Value);
        }
    }

    private void TryRestorePendingState(IntPtr focusedHwnd, bool canConfirmInputMode)
    {
        if (!_pendingRestoreState.HasValue ||
            focusedHwnd == IntPtr.Zero)
        {
            return;
        }

        // A single top-level activation can emit many transient focus events
        // before a real text input target is available. Reapplying the saved
        // mode for every one of them can overwrite a user's manual switch.
        if (!canConfirmInputMode && _restoreAttemptCount > 0)
        {
            return;
        }

        DateTimeOffset now = _nowProvider();
        if (focusedHwnd == _lastRestoreAttemptHwnd && now < _nextRestoreAttemptAt)
        {
            return;
        }

        bool savedIsChinese = _pendingRestoreState.Value;
        TextInputMode desiredMode = savedIsChinese ? TextInputMode.Chinese : TextInputMode.English;
        _lastRestoreAttemptHwnd = focusedHwnd;
        _restoreAttemptCount++;

        if (_fallbackToggleAttempted && canConfirmInputMode)
        {
            if (IsDesiredMode(focusedHwnd, desiredMode))
            {
                if (_currentWindow is WindowKey fallbackWindow)
                {
                    FallbackInputModeApplied?.Invoke(fallbackWindow, desiredMode);
                    _unverifiedFallbackWindows.Remove(fallbackWindow);
                }

                CompleteRestore(focusedHwnd, desiredMode, "Shift fallback verified");
                return;
            }

            DeferOrAbandonRestore(focusedHwnd, desiredMode, "Shift fallback was not verified", now);
            return;
        }

        bool nativeRequestAccepted = _imeService.SetOpenStatus(focusedHwnd, savedIsChinese);
        if (!canConfirmInputMode)
        {
            DiagnosticsLog.Write(
                $"IME state restore requested: window={_currentWindow}, " +
                $"focus=0x{focusedHwnd.ToInt64():X}, mode={desiredMode}, accepted={nativeRequestAccepted}; awaiting validated input target.");
            _nextRestoreAttemptAt = now + RestoreRetryDelay;
            return;
        }

        if (IsDesiredMode(focusedHwnd, desiredMode))
        {
            CompleteRestore(focusedHwnd, desiredMode, "native mode verified");
            return;
        }

        if (now - _restoreStartedAt >= NativeRestoreGracePeriod &&
            (_currentWindow is not WindowKey activeWindow || !_unverifiedFallbackWindows.Contains(activeWindow)) &&
            _imeService.ToggleInputMode(focusedHwnd))
        {
            _fallbackToggleAttempted = true;
            _nextRestoreAttemptAt = now + RestoreRetryDelay;
            DiagnosticsLog.Write(
                $"IME state native restore did not change validated mode; Shift fallback requested: " +
                $"window={_currentWindow}, focus=0x{focusedHwnd.ToInt64():X}, mode={desiredMode}.");
            return;
        }

        DeferOrAbandonRestore(
            focusedHwnd,
            desiredMode,
            nativeRequestAccepted ? "native request accepted but mode mismatch" : "native request failed",
            now);
    }

    private bool IsDesiredMode(IntPtr focusedHwnd, TextInputMode desiredMode)
    {
        TextInputMode currentMode = _imeService.GetInputMode(focusedHwnd);
        if (currentMode != TextInputMode.Unknown)
        {
            return currentMode == desiredMode;
        }

        ImeOpenStatus status = _imeService.GetOpenStatus(focusedHwnd);
        return desiredMode == TextInputMode.English && status == ImeOpenStatus.Closed;
    }

    private void CompleteRestore(IntPtr focusedHwnd, TextInputMode desiredMode, string method)
    {
        DiagnosticsLog.Write(
            $"IME state restored and verified: window={_currentWindow}, " +
            $"focus=0x{focusedHwnd.ToInt64():X}, mode={desiredMode}, method={method}.");
        _pendingRestoreState = null;
        _skipNextStateSave = true;
        _suppressStateSaveUntilWindowChange = false;
    }

    private void DeferOrAbandonRestore(
        IntPtr focusedHwnd,
        TextInputMode desiredMode,
        string reason,
        DateTimeOffset now)
    {
        if (_restoreAttemptCount >= MaxRestoreAttempts)
        {
            DiagnosticsLog.Warn(
                $"IME state restore abandoned after {_restoreAttemptCount} attempts: " +
                $"window={_currentWindow}, focus=0x{focusedHwnd.ToInt64():X}, mode={desiredMode}, reason={reason}.");
            if (_fallbackToggleAttempted && _currentWindow is WindowKey currentWindow)
            {
                _unverifiedFallbackWindows.Add(currentWindow);
            }

            ClearPendingRestore();
            _suppressStateSaveUntilWindowChange = true;
            return;
        }

        _nextRestoreAttemptAt = now + RestoreRetryDelay;
        DiagnosticsLog.Write(
            $"IME state restore deferred: window={_currentWindow}, " +
            $"focus=0x{focusedHwnd.ToInt64():X}, mode={desiredMode}, reason={reason}.");
        Debug.WriteLine(
            $"Failed to verify IME mode {desiredMode} for window {_currentWindow} " +
            $"focus 0x{focusedHwnd.ToInt64():X}; {reason}.");
    }

    private bool? ReadRememberedChineseState(IntPtr focusedHwnd, ImeOpenStatus fallbackStatus) =>
        _imeService.GetInputMode(focusedHwnd) switch
        {
            TextInputMode.Chinese => true,
            TextInputMode.English => false,
            _ => fallbackStatus == ImeOpenStatus.Closed ? false : null
        };

    private void BeginPendingRestore(bool savedIsChinese)
    {
        _pendingRestoreState = savedIsChinese;
        _lastRestoreAttemptHwnd = IntPtr.Zero;
        _restoreAttemptCount = 0;
        _nextRestoreAttemptAt = DateTimeOffset.MinValue;
        _restoreStartedAt = _nowProvider();
        _fallbackToggleAttempted = false;
    }

    private void PreserveUnverifiedFallbackForCurrentWindow()
    {
        if (_fallbackToggleAttempted && _currentWindow is WindowKey currentWindow)
        {
            _unverifiedFallbackWindows.Add(currentWindow);
        }
    }

    private void ClearPendingRestore()
    {
        _pendingRestoreState = null;
        _lastRestoreAttemptHwnd = IntPtr.Zero;
        _restoreAttemptCount = 0;
        _nextRestoreAttemptAt = DateTimeOffset.MinValue;
        _restoreStartedAt = DateTimeOffset.MinValue;
        _fallbackToggleAttempted = false;
    }
}
