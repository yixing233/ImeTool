namespace ImeTool.Caret;

public static class TextPatternCaretPolicy
{
    public static bool CanResolveSelection(
        int selectionCount,
        int startToEndComparison) =>
        selectionCount == 1 && startToEndComparison == 0;
}
