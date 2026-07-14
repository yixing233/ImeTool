using ImeTool.Native;

namespace ImeTool.State;

public readonly record struct WindowKey(IntPtr Hwnd, uint ProcessId)
{
    public override string ToString() => $"0x{Hwnd.ToInt64():X}:{ProcessId}";
}

public interface IWindowInfoService
{
    bool TryGetWindowKey(IntPtr hwnd, out WindowKey key);
    IntPtr GetRootWindow(IntPtr hwnd);
    bool IsWindow(IntPtr hwnd);
    bool IsWindowVisible(IntPtr hwnd);
    bool IsIconic(IntPtr hwnd);
}

public sealed class WindowInfoService : IWindowInfoService
{
    public bool TryGetWindowKey(IntPtr hwnd, out WindowKey key)
    {
        key = default;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        IntPtr root = GetRootWindow(hwnd);
        if (root == IntPtr.Zero || !IsWindow(root))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(root, out uint processId);
        if (processId == 0)
        {
            return false;
        }

        key = new WindowKey(root, processId);
        return true;
    }

    public IntPtr GetRootWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
        return root == IntPtr.Zero ? hwnd : root;
    }

    public bool IsWindow(IntPtr hwnd) => hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd);

    public bool IsWindowVisible(IntPtr hwnd) => hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hwnd);

    public bool IsIconic(IntPtr hwnd) => hwnd != IntPtr.Zero && NativeMethods.IsIconic(hwnd);
}
