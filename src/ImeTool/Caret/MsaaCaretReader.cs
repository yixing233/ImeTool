using System.Runtime.InteropServices;
using Accessibility;
using ImeTool.Native;

namespace ImeTool.Caret;

public static class MsaaCaretReader
{
    private const uint ObjIdCaret = 0xFFFFFFF8;
    private static readonly Guid IAccessibleId = new("618736E0-3C3D-11CF-810C-00AA00389B71");

    public static UiAutomationCaretReadResult Read(IntPtr expectedForeground)
    {
        if (expectedForeground == IntPtr.Zero ||
            NativeMethods.GetForegroundWindow() != expectedForeground)
        {
            return UiAutomationCaretReadResult.Failure("MSAA target window is no longer foreground.");
        }

        IntPtr focusHwnd = GetFocusedHwnd(expectedForeground);
        if (TryReadFromWindow(focusHwnd, out NativeMethods.RECT rect) ||
            (focusHwnd != expectedForeground && TryReadFromWindow(expectedForeground, out rect)))
        {
            return UiAutomationCaretReadResult.Success(new CaretSnapshot(
                focusHwnd,
                focusHwnd,
                rect,
                CaretSource.Msaa));
        }

        return UiAutomationCaretReadResult.Failure(
            "MSAA OBJID_CARET returned no valid caret.",
            focusHwnd: focusHwnd);
    }

    private static bool TryReadFromWindow(IntPtr hwnd, out NativeMethods.RECT rect)
    {
        rect = default;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        object? accessibleObject = null;
        try
        {
            Guid iid = IAccessibleId;
            int result = AccessibleObjectFromWindow(hwnd, ObjIdCaret, ref iid, out accessibleObject);
            if (result < 0 || accessibleObject is not IAccessible accessible)
            {
                return false;
            }

            accessible.accLocation(out int x, out int y, out int width, out int height, 0);
            if (height <= 0 || height > 400 || width > 400 ||
                (x == 0 && y == 0 && width == 0 && height == 0))
            {
                return false;
            }

            rect = new NativeMethods.RECT
            {
                Left = x,
                Top = y,
                Right = x + Math.Max(1, width),
                Bottom = y + height
            };
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
        finally
        {
            if (accessibleObject is not null && Marshal.IsComObject(accessibleObject))
            {
                try
                {
                    Marshal.FinalReleaseComObject(accessibleObject);
                }
                catch
                {
                }
            }
        }
    }

    private static IntPtr GetFocusedHwnd(IntPtr foreground)
    {
        uint threadId = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var info = new NativeMethods.GUITHREADINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>()
        };
        return threadId != 0 && NativeMethods.GetGUIThreadInfo(threadId, ref info) &&
               info.hwndFocus != IntPtr.Zero
            ? info.hwndFocus
            : foreground;
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        IntPtr hwnd,
        uint objectId,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out object accessibleObject);
}
