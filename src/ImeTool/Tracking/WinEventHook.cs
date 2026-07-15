using ImeTool.Native;

namespace ImeTool.Tracking;

public sealed class WinEventHook : IDisposable
{
    private readonly NativeMethods.WinEventDelegate _callback;
    private readonly IntPtr _foregroundHook;
    private readonly IntPtr _focusHook;
    private bool _disposed;

    public WinEventHook()
    {
        _callback = OnWinEvent;
        const uint flags = NativeMethods.WineventOutofcontext | NativeMethods.WineventSkipownprocess;
        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemForeground,
            NativeMethods.EventSystemForeground,
            IntPtr.Zero,
            _callback,
            0,
            0,
            flags);
        _focusHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventObjectFocus,
            NativeMethods.EventObjectFocus,
            IntPtr.Zero,
            _callback,
            0,
            0,
            flags);
    }

    public event EventHandler<IntPtr>? FocusChanged;

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        IntPtr eventRoot = GetRootWindow(hwnd);
        IntPtr foregroundRoot = GetRootWindow(NativeMethods.GetForegroundWindow());
        if (IsForegroundEventTarget(eventRoot, foregroundRoot))
        {
            FocusChanged?.Invoke(this, hwnd);
        }
    }

    internal static bool IsForegroundEventTarget(IntPtr eventRoot, IntPtr foregroundRoot) =>
        eventRoot != IntPtr.Zero && eventRoot == foregroundRoot;

    private static IntPtr GetRootWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
        return root == IntPtr.Zero ? hwnd : root;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_foregroundHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_foregroundHook);
        }

        if (_focusHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_focusHook);
        }
    }
}
