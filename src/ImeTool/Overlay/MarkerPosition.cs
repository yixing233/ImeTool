using ImeTool.Native;

namespace ImeTool.Overlay;

public static class MarkerPosition
{
    public const int DefaultOffsetPixels = 6;

    public static (int X, int Y) FromCaretRect(NativeMethods.RECT caretRect, int offsetPixels = DefaultOffsetPixels) =>
        FromCaretRect(caretRect, offsetPixels, offsetPixels);

    public static (int X, int Y) FromCaretRect(NativeMethods.RECT caretRect, int offsetX, int offsetY)
    {
        // CaretService now accepts only narrow, exact caret rectangles. Anchor
        // to the actual right edge so the visual consistently sits right-below.
        int x = caretRect.Right + offsetX;
        int y = caretRect.Bottom + offsetY;
        return (x, y);
    }
}
