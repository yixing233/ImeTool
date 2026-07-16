using ImeTool.Caret;
using ImeTool.Native;

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

    [Fact]
    public void Adjacent_Character_Edge_Wins_Over_Stale_Collapsed_Address_Bar_Rect()
    {
        var collapsedAtHostStart = new NativeMethods.RECT
        {
            Left = 10,
            Top = 20,
            Right = 11,
            Bottom = 45
        };
        var adjacentCharacterEdge = new NativeMethods.RECT
        {
            Left = 640,
            Top = 20,
            Right = 641,
            Bottom = 45
        };

        Assert.True(TextPatternCaretPolicy.TrySelectCaretRect(
            adjacentCharacterEdge,
            collapsedAtHostStart,
            out NativeMethods.RECT selected));
        Assert.Equal(adjacentCharacterEdge, selected);
    }

    [Fact]
    public void Collapsed_Rect_Is_Used_When_No_Adjacent_Character_Is_Available()
    {
        var collapsed = new NativeMethods.RECT
        {
            Left = 10,
            Top = 20,
            Right = 11,
            Bottom = 45
        };

        Assert.True(TextPatternCaretPolicy.TrySelectCaretRect(
            adjacentCharacterRect: null,
            collapsedRect: collapsed,
            out NativeMethods.RECT selected));
        Assert.Equal(collapsed, selected);
    }
}
