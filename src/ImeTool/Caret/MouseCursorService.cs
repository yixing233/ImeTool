using System.Runtime.InteropServices;
using ImeTool.Native;

namespace ImeTool.Caret;

public readonly record struct MouseCursorSnapshot(
    NativeMethods.POINT Position,
    bool IsVisible,
    bool IsSystemIBeam);

public interface IMouseCursorService
{
    bool TryGetCursor(out MouseCursorSnapshot snapshot);
}

public sealed class MouseCursorService : IMouseCursorService
{
    public bool TryGetCursor(out MouseCursorSnapshot snapshot)
    {
        snapshot = default;
        var info = new NativeMethods.CURSORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.CURSORINFO>()
        };
        if (!NativeMethods.GetCursorInfo(ref info))
        {
            return false;
        }

        IntPtr systemIBeam = NativeMethods.LoadCursor(
            IntPtr.Zero,
            new IntPtr(NativeMethods.OcrIBeam));
        snapshot = new MouseCursorSnapshot(
            info.ptScreenPos,
            (info.flags & NativeMethods.CursorShowing) != 0,
            systemIBeam != IntPtr.Zero && info.hCursor == systemIBeam);
        return true;
    }
}
