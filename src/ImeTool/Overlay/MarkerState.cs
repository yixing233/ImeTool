using ImeTool.Ime;
using ImeTool.Native;
using System.Runtime.InteropServices;
using ImeTool.Diagnostics;

namespace ImeTool.Overlay;

public enum MarkerState
{
    Unknown = 0,
    English = 1,
    Chinese = 2,
    CapsLock = 3
}

public static class MarkerStateResolver
{
    public static MarkerState Resolve(TextInputMode inputMode, bool capsLockOn)
    {
        if (capsLockOn)
        {
            return MarkerState.CapsLock;
        }

        return inputMode switch
        {
            TextInputMode.Chinese => MarkerState.Chinese,
            TextInputMode.English => MarkerState.English,
            _ => MarkerState.Unknown
        };
    }

    public static MarkerState Resolve(ImeOpenStatus imeStatus, bool capsLockOn)
    {
        TextInputMode inputMode = imeStatus switch
        {
            ImeOpenStatus.Open => TextInputMode.Chinese,
            ImeOpenStatus.Closed => TextInputMode.English,
            _ => TextInputMode.Unknown
        };
        return Resolve(inputMode, capsLockOn);
    }
}

public interface ICapsLockService
{
    bool IsCapsLockOn();
}

public sealed class CapsLockService : ICapsLockService, IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _hookProc;
    private IntPtr _hook;
    private bool _isCapsLockOn;
    private bool _capsKeyDown;
    private bool _disposed;

    public CapsLockService()
    {
        _isCapsLockOn = (NativeMethods.GetKeyState(NativeMethods.VkCapital) & 0x0001) != 0;
        _hookProc = OnLowLevelKeyboard;
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            _hookProc,
            NativeMethods.GetModuleHandle(null),
            0);

        if (_hook == IntPtr.Zero)
        {
            DiagnosticsLog.Write($"Caps Lock keyboard hook failed: error={Marshal.GetLastWin32Error()}.");
        }
    }

    public bool IsCapsLockOn() => _hook != IntPtr.Zero
        ? _isCapsLockOn
        : (NativeMethods.GetKeyState(NativeMethods.VkCapital) & 0x0001) != 0;

    private IntPtr OnLowLevelKeyboard(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            NativeMethods.KBDLLHOOKSTRUCT key = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            if ((key.flags & NativeMethods.LlkhfInjected) != 0)
            {
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            int message = wParam.ToInt32();
            if (key.vkCode == NativeMethods.VkCapital)
            {
                if ((message == NativeMethods.WmKeyDown || message == NativeMethods.WmSysKeyDown) && !_capsKeyDown)
                {
                    _capsKeyDown = true;
                    _isCapsLockOn = !_isCapsLockOn;
                    DiagnosticsLog.Write($"Caps Lock state changed: enabled={_isCapsLockOn}.");
                }
                else if (message == NativeMethods.WmKeyUp || message == NativeMethods.WmSysKeyUp)
                {
                    _capsKeyDown = false;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
