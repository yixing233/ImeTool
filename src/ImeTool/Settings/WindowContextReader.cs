using System.Text;
using ImeTool.Native;

namespace ImeTool.Settings;

public readonly record struct WindowContextSnapshot(
    IntPtr RootHwnd,
    IntPtr FocusHwnd,
    string WindowTitle,
    string WindowClass,
    string ControlClass);

public sealed class WindowContextReader
{
    public WindowContextSnapshot Read(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return default;
        }

        IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
        if (root == IntPtr.Zero)
        {
            root = hwnd;
        }

        return new WindowContextSnapshot(
            root,
            hwnd,
            ReadWindowTitle(root),
            ReadClassName(root),
            ReadClassName(hwnd));
    }

    public static string ReadWindowTitle(IntPtr hwnd)
    {
        int length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(length + 1);
        return NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity) > 0
            ? buffer.ToString()
            : string.Empty;
    }

    public static string ReadClassName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(256);
        return NativeMethods.GetClassName(hwnd, buffer, buffer.Capacity) > 0
            ? buffer.ToString()
            : string.Empty;
    }
}
