using ImeTool.Native;

namespace ImeTool.Overlay;

public static class MarkerPosition
{
    public const int DefaultOffsetPixels = 6;

    public static (int X, int Y) FromCaretRect(NativeMethods.RECT caretRect, int offsetPixels = DefaultOffsetPixels) =>
        FromCaretRect(caretRect, offsetPixels, offsetPixels);

    public static (int X, int Y) FromCaretRect(NativeMethods.RECT caretRect, int offsetX, int offsetY)
    {
        // rcCaret.Right is not consistently the caret's right edge. Some UIA
        // providers return a rectangle that extends to the editor boundary or
        // across selected text. Left is the stable insertion-point origin.
        int x = caretRect.Left + offsetX;
        int y = caretRect.Bottom + offsetY;
        return (x, y);
    }
}
