using ImeTool.Native;
using ImeTool.Settings;

namespace ImeTool.Overlay;

public sealed class MarkerVisibilityController
{
    private bool _hasObservation;
    private IntPtr _lastFocusHwnd;
    private MarkerState _lastStatus = MarkerState.Unknown;
    private NativeMethods.RECT _lastCaretRect;
    private DateTimeOffset _visibleUntil;

    public bool ShouldShow(
        DateTimeOffset now,
        MarkerBehaviorSettings settings,
        IntPtr focusHwnd,
        MarkerState status,
        NativeMethods.RECT caretRect)
    {
        MarkerBehaviorSettings normalized = settings.Normalize();
        bool firstObservation = !_hasObservation;
        bool focusChanged = _hasObservation && focusHwnd != _lastFocusHwnd;
        bool statusChanged = _hasObservation && status != _lastStatus;
        bool caretMoved = _hasObservation && !RectsEqual(caretRect, _lastCaretRect);

        _hasObservation = true;
        _lastFocusHwnd = focusHwnd;
        _lastStatus = status;
        _lastCaretRect = caretRect;

        if (normalized.DisplayMode == MarkerDisplayMode.Always)
        {
            return true;
        }

        bool activity = normalized.DisplayMode switch
        {
            MarkerDisplayMode.OnImeChange => firstObservation || focusChanged || statusChanged,
            MarkerDisplayMode.WhileTyping => firstObservation || focusChanged || statusChanged || caretMoved,
            _ => true
        };

        if (activity)
        {
            _visibleUntil = now.AddMilliseconds(normalized.AutoHideDelayMilliseconds);
        }

        return now < _visibleUntil;
    }

    public void Reset()
    {
        _hasObservation = false;
        _lastFocusHwnd = IntPtr.Zero;
        _lastStatus = MarkerState.Unknown;
        _lastCaretRect = default;
        _visibleUntil = default;
    }

    private static bool RectsEqual(NativeMethods.RECT left, NativeMethods.RECT right) =>
        left.Left == right.Left &&
        left.Top == right.Top &&
        left.Right == right.Right &&
        left.Bottom == right.Bottom;
}
