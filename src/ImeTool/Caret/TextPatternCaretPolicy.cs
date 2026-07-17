using ImeTool.Native;

namespace ImeTool.Caret;

public static class TextPatternCaretPolicy
{
    public static bool CanResolveSelection(
        int selectionCount,
        int startToEndComparison) =>
        selectionCount == 1 && startToEndComparison == 0;

    public static bool TrySelectCaretRect(
        NativeMethods.RECT? adjacentCharacterRect,
        NativeMethods.RECT? collapsedRect,
        out NativeMethods.RECT selected)
    {
        if (adjacentCharacterRect is NativeMethods.RECT adjacent)
        {
            selected = adjacent;
            return true;
        }

        if (collapsedRect is NativeMethods.RECT collapsed)
        {
            selected = collapsed;
            return true;
        }

        selected = default;
        return false;
    }

    public static bool IsPlaceholderDocument(
        bool valueIsKnown,
        string? value,
        string documentText) =>
        valueIsKnown &&
        string.IsNullOrEmpty(value) &&
        !string.IsNullOrEmpty(documentText);

    public static bool IsUsableAdjacentCharacterRect(
        System.Windows.Rect characterRect,
        NativeMethods.RECT? textHostBounds)
    {
        if (textHostBounds is not NativeMethods.RECT host)
        {
            return true;
        }

        if (CaretGeometry.IsWholeTextHostRectangle(characterRect, host))
        {
            return false;
        }

        double horizontalOverlap = Math.Min(characterRect.Right, host.Right) -
                                   Math.Max(characterRect.Left, host.Left);
        double verticalOverlap = Math.Min(characterRect.Bottom, host.Bottom) -
                                 Math.Max(characterRect.Top, host.Top);
        return horizontalOverlap > 0 && verticalOverlap > 0;
    }
}
