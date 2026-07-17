using ImeTool.Native;

namespace ImeTool.Caret;

public static class CaretGeometry
{
    private const int MinimumReliableNativeCaretHeight = 8;
    private const int BoundsTolerance = 4;
    private const double MaximumEmptySingleLineHostHeight = 64;
    private const int MaximumBrowserSingleLineHostHeight = 80;

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

    public static bool TryNormalizeNativeRect(
        NativeMethods.RECT source,
        NativeMethods.RECT? textHostBounds,
        out NativeMethods.RECT rect)
    {
        rect = source;
        if (source.Width <= 0 || source.Height <= 0 || source.Height > 200)
        {
            return false;
        }

        if (source.Height < MinimumReliableNativeCaretHeight)
        {
            return false;
        }

        if (textHostBounds is not NativeMethods.RECT host)
        {
            return true;
        }

        if (host.Width <= 0 || host.Height <= 0)
        {
            return true;
        }

        return source.Left >= host.Left - BoundsTolerance &&
               source.Right <= host.Right + BoundsTolerance &&
               source.Top >= host.Top - BoundsTolerance &&
               source.Bottom <= host.Bottom + BoundsTolerance;
    }

    public static bool TryCreateFromTextEdge(
        System.Windows.Rect source,
        bool trailingEdge,
        out NativeMethods.RECT rect)
    {
        rect = default;
        if (!IsUsableScreenRect(source))
        {
            return false;
        }

        double edge = trailingEdge ? source.Right : source.Left;
        int left = (int)Math.Round(edge);
        int top = (int)Math.Round(source.Top);
        int bottom = Math.Max(top + 1, (int)Math.Round(source.Bottom));
        rect = new NativeMethods.RECT
        {
            Left = left,
            Top = top,
            Right = left + 1,
            Bottom = bottom
        };
        return true;
    }

    public static bool TryCreateEmptyTextHostRect(
        System.Windows.Rect source,
        out NativeMethods.RECT rect)
    {
        rect = default;
        if (!IsUsableScreenRect(source) ||
            source.Height > MaximumEmptySingleLineHostHeight)
        {
            return false;
        }

        int left = (int)Math.Round(source.Left);
        int top = (int)Math.Round(source.Top);
        int bottom = Math.Max(top + 1, (int)Math.Round(source.Bottom));
        rect = new NativeMethods.RECT
        {
            Left = left,
            Top = top,
            Right = left + 1,
            Bottom = bottom
        };
        return true;
    }

    public static bool IsWithinTextHost(
        NativeMethods.RECT caret,
        NativeMethods.RECT host)
    {
        if (caret.Width <= 0 || caret.Height <= 0 ||
            host.Width <= 0 || host.Height <= 0)
        {
            return false;
        }

        return caret.Left >= host.Left - BoundsTolerance &&
               caret.Right <= host.Right + BoundsTolerance &&
               caret.Top >= host.Top - BoundsTolerance &&
               caret.Bottom <= host.Bottom + BoundsTolerance;
    }

    public static bool TryNormalizeBrowserMsaaRect(
        NativeMethods.RECT caret,
        NativeMethods.RECT host,
        out NativeMethods.RECT normalized)
    {
        normalized = default;
        if (caret.Width <= 0 || caret.Width > 32 ||
            caret.Height < MinimumReliableNativeCaretHeight || caret.Height > 200 ||
            host.Width <= 0 || host.Height <= 0)
        {
            return false;
        }

        bool xIsInside = caret.Left >= host.Left - BoundsTolerance &&
                         caret.Left <= host.Right + BoundsTolerance;
        int verticalOverlap = Math.Min(caret.Bottom, host.Bottom) -
                              Math.Max(caret.Top, host.Top);
        if (!xIsInside || verticalOverlap <= 0)
        {
            return false;
        }

        int width = Math.Max(1, Math.Min(caret.Width, host.Width));
        int left = Math.Clamp(caret.Left, host.Left, Math.Max(host.Left, host.Right - width));
        int top = caret.Top;
        int height = caret.Height;
        if (host.Height <= MaximumBrowserSingleLineHostHeight)
        {
            height = Math.Min(height, host.Height);
            top = host.Top + ((host.Height - height) / 2);
        }
        else if (!IsWithinTextHost(caret, host))
        {
            return false;
        }

        normalized = new NativeMethods.RECT
        {
            Left = left,
            Top = top,
            Right = left + width,
            Bottom = top + height
        };
        return true;
    }

    public static bool IsWholeTextHostRectangle(
        System.Windows.Rect source,
        NativeMethods.RECT host)
    {
        if (!IsUsableScreenRect(source) || host.Width <= 0 || host.Height <= 0)
        {
            return false;
        }

        return source.Left <= host.Left + BoundsTolerance &&
               source.Right >= host.Right - BoundsTolerance &&
               source.Top <= host.Top + BoundsTolerance &&
               source.Bottom >= host.Bottom - BoundsTolerance;
    }

    private static bool IsUsableScreenRect(System.Windows.Rect source) =>
        !source.IsEmpty &&
        source.Width > 0 &&
        source.Height > 0 &&
        double.IsFinite(source.Left) &&
        double.IsFinite(source.Top) &&
        double.IsFinite(source.Right) &&
        double.IsFinite(source.Bottom);
}
