using System.Runtime.InteropServices;
using ImeTool.Diagnostics;
using ImeTool.Native;

namespace ImeTool.Ime;

public static class InputModeToggleSender
{
    private const ushort VkShift = NativeMethods.VkShift;

    public static bool TrySendShift(IntPtr targetHwnd)
    {
        if (targetHwnd == IntPtr.Zero ||
            !AreModifierKeysReleased() ||
            !IsCurrentFocusedTarget(targetHwnd))
        {
            return false;
        }

        NativeMethods.INPUT[] inputs =
        [
            CreateKeyboardInput(VkShift, keyUp: false),
            CreateKeyboardInput(VkShift, keyUp: true)
        ];
        uint sent = NativeMethods.SendInput(
            checked((uint)inputs.Length),
            inputs,
            Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length)
        {
            DiagnosticsLog.Warn($"Synthetic Shift input-mode fallback failed: sent={sent}, error={Marshal.GetLastWin32Error()}.");
            return false;
        }

        DiagnosticsLog.Write($"Synthetic Shift input-mode fallback sent to hwnd=0x{targetHwnd.ToInt64():X}.");
        return true;
    }

    internal static bool IsSameRootWindow(IntPtr targetRoot, IntPtr foregroundRoot) =>
        targetRoot != IntPtr.Zero && targetRoot == foregroundRoot;

    internal static bool IsKeyDown(short state) => (state & 0x8000) != 0;

    private static bool IsCurrentFocusedTarget(IntPtr hwnd)
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        IntPtr targetRoot = GetRoot(hwnd);
        IntPtr foregroundRoot = GetRoot(foreground);
        if (!IsSameRootWindow(targetRoot, foregroundRoot))
        {
            return false;
        }

        uint foregroundThreadId = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        if (foregroundThreadId == 0)
        {
            return false;
        }

        var info = new NativeMethods.GUITHREADINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>()
        };
        return NativeMethods.GetGUIThreadInfo(foregroundThreadId, ref info) && info.hwndFocus == hwnd;
    }

    private static bool AreModifierKeysReleased() =>
        !IsKeyDown(NativeMethods.GetAsyncKeyState(NativeMethods.VkShift)) &&
        !IsKeyDown(NativeMethods.GetAsyncKeyState(NativeMethods.VkControl)) &&
        !IsKeyDown(NativeMethods.GetAsyncKeyState(NativeMethods.VkMenu)) &&
        !IsKeyDown(NativeMethods.GetAsyncKeyState(NativeMethods.VkLwin)) &&
        !IsKeyDown(NativeMethods.GetAsyncKeyState(NativeMethods.VkRwin));

    private static IntPtr GetRoot(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
        return root == IntPtr.Zero ? hwnd : root;
    }

    private static NativeMethods.INPUT CreateKeyboardInput(ushort virtualKey, bool keyUp) => new()
    {
        type = NativeMethods.InputKeyboard,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = virtualKey,
                dwFlags = keyUp ? NativeMethods.KeyeventfKeyup : 0
            }
        }
    };
}
