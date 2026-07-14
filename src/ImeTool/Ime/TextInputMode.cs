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
