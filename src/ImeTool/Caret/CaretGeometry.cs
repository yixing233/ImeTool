using ImeTool.Native;

namespace ImeTool.Caret;

public static class CaretGeometry
{
    public static bool TryCreateExactRect(System.Windows.Rect source, out NativeMethods.RECT rect)
    {
        rect = default;
        if (source.IsEmpty ||
            source.Height <= 0 ||
            !double.IsFinite(source.Left) ||
            !double.IsFinite(source.Top) ||
            !double.IsFinite(source.Width) ||
            !double.IsFinite(source.Height) ||
            !double.IsFinite(source.Right) ||
            !double.IsFinite(source.Bottom))
        {
            return false;
        }

        double width = Math.Max(1, source.Width);
        double maximumCaretWidth = Math.Max(8, source.Height * 0.5);
        if (width > maximumCaretWidth)
        {
            return false;
        }

        int left = (int)Math.Round(source.Left);
        int top = (int)Math.Round(source.Top);
        int right = Math.Max(left + 1, (int)Math.Round(source.Right));
        int bottom = Math.Max(top + 1, (int)Math.Round(source.Bottom));
        rect = new NativeMethods.RECT
        {
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom
        };
        return true;
    }
}
