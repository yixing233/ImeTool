using ImeTool.Caret;

namespace ImeTool.Tests.Caret;

public sealed class TextPatternCaretPolicyTests
{
    [Theory]
    [InlineData(1, 0, true)]
    [InlineData(1, -1, false)]
    [InlineData(1, 1, false)]
    [InlineData(2, 0, false)]
    [InlineData(0, 0, false)]
    public void OnlySingleCollapsedSelectionCanResolveCaret(
        int selectionCount,
        int startToEndComparison,
        bool expected)
    {
        Assert.Equal(
            expected,
            TextPatternCaretPolicy.CanResolveSelection(
                selectionCount,
                startToEndComparison));
    }
}
