using ImeTool.Native;

namespace ImeTool.Ime;

/// <summary>
/// Chooses the IME signal that is actually changing instead of assuming that
/// a particular keyboard shortcut toggled the input mode.
/// </summary>
public sealed class ImeModeSignalTracker
{
    private SignalSource _source = SignalSource.ConversionMode;
    private IntPtr _lastHwnd;
    private ImeOpenStatus _lastOpenStatus;
    private uint _lastConversionMode;
    private bool _hasReading;

    public ImeDetectionSource LastSource { get; private set; } = ImeDetectionSource.Fallback;

    public TextInputMode Resolve(
        IntPtr hwnd,
        bool openStatusKnown,
        ImeOpenStatus openStatus,
        bool conversionModeKnown,
        uint conversionMode,
        TextInputMode fallbackMode)
    {
        if (hwnd == IntPtr.Zero ||
            !openStatusKnown ||
            openStatus == ImeOpenStatus.Unknown ||
            !conversionModeKnown)
        {
            LastSource = ImeDetectionSource.Fallback;
            return fallbackMode;
        }

        TextInputMode openStatusMode = openStatus == ImeOpenStatus.Open
            ? TextInputMode.Chinese
            : TextInputMode.English;
        TextInputMode conversionModeResult =
            (conversionMode & NativeMethods.ImeCmodeNative) != 0
                ? TextInputMode.Chinese
                : TextInputMode.English;

        if (!_hasReading || _lastHwnd != hwnd)
        {
            StoreReading(hwnd, openStatus, conversionMode);
            return Select(openStatusMode, conversionModeResult);
        }

        bool openStatusChanged = openStatus != _lastOpenStatus;
        bool conversionModeChanged = conversionMode != _lastConversionMode;
        if (openStatusChanged && !conversionModeChanged)
        {
            _source = SignalSource.OpenStatus;
        }
        else if (conversionModeChanged && !openStatusChanged)
        {
            _source = SignalSource.ConversionMode;
        }

        StoreReading(hwnd, openStatus, conversionMode);
        return Select(openStatusMode, conversionModeResult);
    }

    private TextInputMode Select(
        TextInputMode openStatusMode,
        TextInputMode conversionModeResult)
    {
        LastSource = _source == SignalSource.OpenStatus
            ? ImeDetectionSource.OpenStatus
            : ImeDetectionSource.ConversionMode;
        return _source == SignalSource.OpenStatus ? openStatusMode : conversionModeResult;
    }

    private void StoreReading(IntPtr hwnd, ImeOpenStatus openStatus, uint conversionMode)
    {
        _lastHwnd = hwnd;
        _lastOpenStatus = openStatus;
        _lastConversionMode = conversionMode;
        _hasReading = true;
    }

    private enum SignalSource
    {
        ConversionMode,
        OpenStatus
    }
}
