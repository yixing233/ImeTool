using System.Diagnostics;
using ImeTool.Native;
using ImeTool.Settings;

namespace ImeTool.Ime;

public interface IImeService
{
    ImeOpenStatus GetOpenStatus(IntPtr hwnd);
    bool SetOpenStatus(IntPtr hwnd, bool isOpen);

    bool ToggleInputMode(IntPtr hwnd) => false;

    TextInputMode GetInputMode(IntPtr hwnd) =>
        TextInputModeResolver.Resolve(GetOpenStatus(hwnd), conversionModeKnown: false, conversionMode: 0);
}

public sealed class ImeService : IImeService, IImeDiagnosticService, IDisposable
{
    private const uint ImeMessageTimeoutMilliseconds = 25;
    private readonly ITsfImeService _tsfService;
    private readonly ImeModeSignalTracker _modeSignalTracker = new();
    private ImeDetectionRuleMatcher _detectionRuleMatcher = new(null);

    public ImeService()
        : this(new TsfImeService())
    {
    }

    public ImeService(ITsfImeService tsfService)
    {
        _tsfService = tsfService;
    }

    public ImeOpenStatus GetOpenStatus(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return ImeOpenStatus.Unknown;
        }

        return ImeStatusResolver.FirstKnown(
            () => GetOpenStatusFromContext(hwnd),
            () => GetOpenStatusFromDefaultImeWindow(hwnd),
            () => _tsfService.GetOpenStatus(hwnd));
    }

    public TextInputMode GetInputMode(IntPtr hwnd) => ReadDiagnostics(hwnd).FinalMode;

    public void SetDetectionRules(IReadOnlyList<ImeDetectionRule> rules) =>
        _detectionRuleMatcher = new ImeDetectionRuleMatcher(rules);

    public ImeDiagnosticSnapshot ReadDiagnostics(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return new ImeDiagnosticSnapshot();
        }

        uint threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        IntPtr keyboardLayoutHandle = threadId == 0
            ? IntPtr.Zero
            : NativeMethods.GetKeyboardLayout(threadId);
        ulong keyboardLayoutValue = unchecked((ulong)keyboardLayoutHandle.ToInt64());
        string keyboardLayout = $"0x{keyboardLayoutValue:X16}";
        ushort languageId = unchecked((ushort)(keyboardLayoutHandle.ToInt64() & 0xFFFF));
        bool isChineseInputMethod = TsfImeService.IsChineseLanguageId(languageId);
        ImeOpenStatus defaultImeOpenStatus = ImeOpenStatus.Unknown;
        long? defaultImeOpenStatusCode = null;
        uint defaultImeConversionMode = 0;
        bool defaultImeOpenStatusKnown = false;
        bool defaultImeConversionKnown = false;
        if (isChineseInputMethod)
        {
            defaultImeOpenStatusKnown =
                TryGetOpenStatusCodeFromDefaultImeWindow(hwnd, out long openStatusCode);
            if (defaultImeOpenStatusKnown)
            {
                defaultImeOpenStatusCode = openStatusCode;
                defaultImeOpenStatus = openStatusCode == 0
                    ? ImeOpenStatus.Closed
                    : ImeOpenStatus.Open;
            }

            defaultImeConversionKnown =
                TryGetConversionModeFromDefaultImeWindow(hwnd, out defaultImeConversionMode);
        }

        _ = TryGetInputModeFromContext(hwnd, out TextInputMode contextMode);
        ImeOpenStatus openStatus = defaultImeOpenStatusKnown
            ? defaultImeOpenStatus
            : GetOpenStatus(hwnd);
        TextInputMode fallbackMode = TextInputModeReadingResolver.Resolve(
            isChineseInputMethod,
            defaultImeConversionKnown,
            defaultImeConversionMode,
            contextMode,
            openStatus);

        ImeDetectionRule? customRule = _detectionRuleMatcher.Match(
            keyboardLayout,
            defaultImeOpenStatusCode,
            defaultImeConversionKnown ? defaultImeConversionMode : null);
        TextInputMode finalMode;
        ImeDetectionSource source;
        if (customRule is not null)
        {
            finalMode = customRule.Result;
            source = ImeDetectionSource.CustomRule;
        }
        else if (isChineseInputMethod)
        {
            finalMode = _modeSignalTracker.Resolve(
                hwnd,
                defaultImeOpenStatusKnown,
                defaultImeOpenStatus,
                defaultImeConversionKnown,
                defaultImeConversionMode,
                fallbackMode);
            source = _modeSignalTracker.LastSource == ImeDetectionSource.Fallback &&
                     contextMode != TextInputMode.Unknown
                ? ImeDetectionSource.Context
                : _modeSignalTracker.LastSource;
        }
        else
        {
            finalMode = fallbackMode;
            source = ImeDetectionSource.NonChineseLayout;
        }

        return new ImeDiagnosticSnapshot
        {
            FocusHwnd = hwnd,
            ProcessId = processId,
            ThreadId = threadId,
            KeyboardLayout = keyboardLayout,
            LanguageId = languageId,
            IsChineseInputMethod = isChineseInputMethod,
            OpenStatusCode = defaultImeOpenStatusCode,
            ConversionMode = defaultImeConversionKnown ? defaultImeConversionMode : null,
            ContextMode = contextMode,
            OpenStatus = openStatus,
            FinalMode = finalMode,
            Source = source,
            MatchedRuleDescription = customRule is null
                ? null
                : $"{customRule.KeyboardLayout} / open={customRule.OpenStatusCode?.ToString() ?? "*"} / conversion={customRule.ConversionMode?.ToString() ?? "*"}"
        };
    }

    public bool SetOpenStatus(IntPtr hwnd, bool isOpen)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        return ImeStatusResolver.FirstSuccessful(
            () => SetOpenStatusWithContext(hwnd, isOpen),
            () => SetOpenStatusWithDefaultImeWindow(hwnd, isOpen),
            () => _tsfService.SetOpenStatus(hwnd, isOpen));
    }

    public bool ToggleInputMode(IntPtr hwnd) =>
        TsfImeService.IsChineseInputMethod(hwnd) && InputModeToggleSender.TrySendShift(hwnd);

    public void Dispose()
    {
        if (_tsfService is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static ImeOpenStatus GetOpenStatusFromContext(IntPtr hwnd)
    {
        IntPtr context = NativeMethods.ImmGetContext(hwnd);
        if (context == IntPtr.Zero)
        {
            return ImeOpenStatus.Unknown;
        }

        try
        {
            return NativeMethods.ImmGetOpenStatus(context) ? ImeOpenStatus.Open : ImeOpenStatus.Closed;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ImmGetOpenStatus failed for 0x{hwnd.ToInt64():X}: {ex}");
            return ImeOpenStatus.Unknown;
        }
        finally
        {
            NativeMethods.ImmReleaseContext(hwnd, context);
        }
    }

    private static bool TryGetInputModeFromContext(IntPtr hwnd, out TextInputMode mode)
    {
        mode = TextInputMode.Unknown;
        IntPtr context = NativeMethods.ImmGetContext(hwnd);
        if (context == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            ImeOpenStatus openStatus = NativeMethods.ImmGetOpenStatus(context)
                ? ImeOpenStatus.Open
                : ImeOpenStatus.Closed;
            bool conversionKnown = NativeMethods.ImmGetConversionStatus(
                context,
                out uint conversionMode,
                out _);
            mode = conversionKnown
                ? TextInputModeResolver.Resolve(openStatus, conversionModeKnown: true, conversionMode: conversionMode)
                : openStatus == ImeOpenStatus.Closed
                    ? TextInputMode.English
                    : TextInputMode.Unknown;
            return mode != TextInputMode.Unknown;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"IME input-mode read failed for 0x{hwnd.ToInt64():X}: {ex}");
            return false;
        }
        finally
        {
            NativeMethods.ImmReleaseContext(hwnd, context);
        }
    }

    private static ImeOpenStatus GetOpenStatusFromDefaultImeWindow(IntPtr hwnd)
    {
        return TryGetOpenStatusCodeFromDefaultImeWindow(hwnd, out long code)
            ? code == 0 ? ImeOpenStatus.Closed : ImeOpenStatus.Open
            : ImeOpenStatus.Unknown;
    }

    private static bool TryGetOpenStatusCodeFromDefaultImeWindow(IntPtr hwnd, out long openStatusCode)
    {
        openStatusCode = 0;
        try
        {
            IntPtr imeWindow = NativeMethods.ImmGetDefaultIMEWnd(hwnd);
            if (imeWindow == IntPtr.Zero)
            {
                return false;
            }

            IntPtr sent = NativeMethods.SendMessageTimeout(
                imeWindow,
                NativeMethods.WmImeControl,
                NativeMethods.ImcGetOpenStatus,
                IntPtr.Zero,
                NativeMethods.SmtoAbortIfHung,
                ImeMessageTimeoutMilliseconds,
                out IntPtr result);
            if (sent == IntPtr.Zero)
            {
                return false;
            }

            openStatusCode = result.ToInt64();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Default IME window get status failed for 0x{hwnd.ToInt64():X}: {ex}");
            return false;
        }
    }

    private static bool TryGetConversionModeFromDefaultImeWindow(
        IntPtr hwnd,
        out uint conversionMode)
    {
        conversionMode = 0;
        try
        {
            IntPtr imeWindow = NativeMethods.ImmGetDefaultIMEWnd(hwnd);
            if (imeWindow == IntPtr.Zero)
            {
                return false;
            }

            IntPtr sent = NativeMethods.SendMessageTimeout(
                imeWindow,
                NativeMethods.WmImeControl,
                NativeMethods.ImcGetConversionMode,
                IntPtr.Zero,
                NativeMethods.SmtoAbortIfHung,
                ImeMessageTimeoutMilliseconds,
                out IntPtr result);
            if (sent == IntPtr.Zero)
            {
                return false;
            }

            conversionMode = unchecked((uint)result.ToInt64());
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Default IME window conversion-mode read failed for 0x{hwnd.ToInt64():X}: {ex}");
            return false;
        }
    }

    private static bool SetOpenStatusWithContext(IntPtr hwnd, bool isOpen)
    {
        IntPtr context = NativeMethods.ImmGetContext(hwnd);
        if (context == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            bool ok = NativeMethods.ImmSetOpenStatus(context, isOpen);
            if (!ok)
            {
                Debug.WriteLine($"ImmSetOpenStatus failed for 0x{hwnd.ToInt64():X}, isOpen={isOpen}.");
            }

            return ok;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ImmSetOpenStatus threw for 0x{hwnd.ToInt64():X}: {ex}");
            return false;
        }
        finally
        {
            NativeMethods.ImmReleaseContext(hwnd, context);
        }
    }

    private static bool SetOpenStatusWithDefaultImeWindow(IntPtr hwnd, bool isOpen)
    {
        try
        {
            IntPtr imeWindow = NativeMethods.ImmGetDefaultIMEWnd(hwnd);
            if (imeWindow == IntPtr.Zero)
            {
                Debug.WriteLine($"ImmGetDefaultIMEWnd returned null for 0x{hwnd.ToInt64():X}; cannot set IME status.");
                return false;
            }

            IntPtr sent = NativeMethods.SendMessageTimeout(
                imeWindow,
                NativeMethods.WmImeControl,
                NativeMethods.ImcSetOpenStatus,
                isOpen ? new IntPtr(1) : IntPtr.Zero,
                NativeMethods.SmtoAbortIfHung,
                ImeMessageTimeoutMilliseconds,
                out _);
            return sent != IntPtr.Zero;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Default IME window set status failed for 0x{hwnd.ToInt64():X}: {ex}");
            return false;
        }
    }
}

