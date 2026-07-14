using System.Diagnostics;
using System.Runtime.InteropServices;
using ImeTool.Native;

namespace ImeTool.Ime;

public interface ITsfImeService
{
    ImeOpenStatus GetOpenStatus(IntPtr hwnd);
    bool SetOpenStatus(IntPtr hwnd, bool isOpen);
}

public sealed class TsfImeService : ITsfImeService, IDisposable
{
    private const int ChinesePrimaryLanguageId = 0x04;
    private TsfInterop.ITfThreadMgr? _threadManager;
    private TsfInterop.ITfCompartment? _openCloseCompartment;
    private uint _clientId;
    private bool _initializationAttempted;
    private bool _disposed;

    public ImeOpenStatus GetOpenStatus(IntPtr hwnd)
    {
        if (_disposed || hwnd == IntPtr.Zero)
        {
            return ImeOpenStatus.Unknown;
        }

        if (!TryGetTargetLanguage(hwnd, out ushort languageId))
        {
            return ImeOpenStatus.Unknown;
        }

        if (!IsChineseLanguageId(languageId))
        {
            return ImeOpenStatus.Closed;
        }

        if (!EnsureInitialized() || _openCloseCompartment is null)
        {
            return ImeOpenStatus.Unknown;
        }

        try
        {
            int result = _openCloseCompartment.GetValue(out object value);
            if (result < 0 || value is null)
            {
                return ImeOpenStatus.Unknown;
            }

            return Convert.ToInt32(value) != 0 ? ImeOpenStatus.Open : ImeOpenStatus.Closed;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"TSF open-close compartment read failed: {exception}");
            return ImeOpenStatus.Unknown;
        }
    }

    public bool SetOpenStatus(IntPtr hwnd, bool isOpen)
    {
        if (_disposed || hwnd == IntPtr.Zero || !TryGetTargetLanguage(hwnd, out ushort languageId))
        {
            return false;
        }

        if (!IsChineseLanguageId(languageId))
        {
            return !isOpen;
        }

        if (!EnsureInitialized() || _openCloseCompartment is null)
        {
            return false;
        }

        try
        {
            object value = isOpen ? 1 : 0;
            return _openCloseCompartment.SetValue(_clientId, ref value) >= 0;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"TSF open-close compartment write failed: {exception}");
            return false;
        }
    }

    public static bool IsChineseLanguageId(ushort languageId) =>
        (languageId & 0x03FF) == ChinesePrimaryLanguageId;

    public static bool IsChineseInputMethod(IntPtr hwnd) =>
        TryGetTargetLanguage(hwnd, out ushort languageId) && IsChineseLanguageId(languageId);

    private static bool TryGetTargetLanguage(IntPtr hwnd, out ushort languageId)
    {
        languageId = 0;
        uint threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        if (threadId == 0)
        {
            return false;
        }

        IntPtr keyboardLayout = NativeMethods.GetKeyboardLayout(threadId);
        if (keyboardLayout == IntPtr.Zero)
        {
            return false;
        }

        languageId = unchecked((ushort)(keyboardLayout.ToInt64() & 0xFFFF));
        return true;
    }

    private bool EnsureInitialized()
    {
        if (_openCloseCompartment is not null)
        {
            return true;
        }

        if (_initializationAttempted)
        {
            return false;
        }

        _initializationAttempted = true;
        try
        {
            int result = TsfInterop.TF_CreateThreadMgr(out TsfInterop.ITfThreadMgr threadManager);
            if (result < 0)
            {
                return false;
            }

            _threadManager = threadManager;
            result = threadManager.Activate(out _clientId);
            if (result < 0)
            {
                return false;
            }

            if (threadManager is not TsfInterop.ITfCompartmentMgr compartmentManager)
            {
                return false;
            }

            Guid compartmentId = TsfInterop.KeyboardOpenCloseCompartment;
            result = compartmentManager.GetCompartment(ref compartmentId, out TsfInterop.ITfCompartment compartment);
            if (result < 0)
            {
                return false;
            }

            _openCloseCompartment = compartment;
            return true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"TSF initialization failed: {exception}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseComObject(_openCloseCompartment);
        _openCloseCompartment = null;

        if (_threadManager is not null)
        {
            try
            {
                _threadManager.Deactivate();
            }
            catch
            {
                // Shutdown must continue even when TSF has already torn down.
            }

            ReleaseComObject(_threadManager);
            _threadManager = null;
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
