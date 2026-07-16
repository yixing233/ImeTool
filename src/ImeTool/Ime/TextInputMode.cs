using ImeTool.Native;

namespace ImeTool.Ime;

public enum TextInputMode
{
    Unknown = 0,
    English = 1,
    Chinese = 2
}

public static class TextInputModeResolver
{
    public static TextInputMode Resolve(
        ImeOpenStatus openStatus,
        bool conversionModeKnown,
        uint conversionMode)
    {
        if (openStatus == ImeOpenStatus.Closed)
        {
            return TextInputMode.English;
        }

        if (openStatus != ImeOpenStatus.Open)
        {
            return TextInputMode.Unknown;
        }

        if (!conversionModeKnown)
        {
            return TextInputMode.Chinese;
        }

        return (conversionMode & NativeMethods.ImeCmodeNative) != 0
            ? TextInputMode.Chinese
            : TextInputMode.English;
    }
}

public static class TextInputModeReadingResolver
{
    public static TextInputMode Resolve(
        bool isChineseInputMethod,
        bool defaultImeConversionKnown,
        uint defaultImeConversionMode,
        TextInputMode contextMode,
        ImeOpenStatus openStatus)
    {
        // Modern TSF applications can expose a stale IMM context while the
        // default IME window still reports the mode shown by the system tray.
        if (isChineseInputMethod && defaultImeConversionKnown)
        {
            return (defaultImeConversionMode & NativeMethods.ImeCmodeNative) != 0
                ? TextInputMode.Chinese
                : TextInputMode.English;
        }

        if (contextMode != TextInputMode.Unknown)
        {
            return contextMode;
        }

        return openStatus == ImeOpenStatus.Closed
            ? TextInputMode.English
            : TextInputMode.Unknown;
    }
}
