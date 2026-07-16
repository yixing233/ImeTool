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
}
