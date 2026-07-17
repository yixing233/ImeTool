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

    [Fact]
    public void Empty_Value_With_Placeholder_Text_Is_Rejected()
    {
        Assert.True(TextPatternCaretPolicy.IsPlaceholderDocument(
            valueIsKnown: true,
            value: string.Empty,
            documentText: "Search settings"));
    }

    [Fact]
    public void Empty_Value_With_Chrome_Object_Replacement_Text_Is_Rejected()
    {
        Assert.True(TextPatternCaretPolicy.IsPlaceholderDocument(
            valueIsKnown: true,
            value: string.Empty,
            documentText: "\uFFFC"));
    }

    [Fact]
    public void Actual_Text_Is_Not_Classified_As_Placeholder()
    {
        Assert.False(TextPatternCaretPolicy.IsPlaceholderDocument(
            valueIsKnown: true,
            value: "query",
            documentText: "query"));
    }

    [Fact]
    public void Whole_Control_Rectangle_Is_Not_Used_As_Adjacent_Character()
    {
        var host = new NativeMethods.RECT
        {
            Left = 79,
            Top = 566,
            Right = 479,
            Bottom = 613
        };

        Assert.False(TextPatternCaretPolicy.IsUsableAdjacentCharacterRect(
            new System.Windows.Rect(79, 566, 400, 47),
            host));
    }

    [Fact]
    public void Adjacent_Dom_Rectangle_Outside_Text_Host_Is_Rejected()
    {
        var host = new NativeMethods.RECT
        {
            Left = 391,
            Top = 313,
            Right = 1090,
            Bottom = 379
        };

        Assert.False(TextPatternCaretPolicy.IsUsableAdjacentCharacterRect(
            new System.Windows.Rect(505, 505, 1, 2),
            host));
        Assert.True(TextPatternCaretPolicy.IsUsableAdjacentCharacterRect(
            new System.Windows.Rect(495, 333, 10, 26),
            host));
    }
}
