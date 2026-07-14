using ImeTool.Native;
using ImeTool.Overlay;

namespace ImeTool.Tests.Overlay;

public sealed class MarkerPositionTests
{
    [Fact]
    public void FromCaretRect_Uses_Caret_Origin_And_Bottom_With_Default_Offset()
    {
        var rect = new NativeMethods.RECT { Left = 10, Top = 20, Right = 11, Bottom = 40 };

        (int x, int y) = MarkerPosition.FromCaretRect(rect);

        Assert.Equal(17, x);
        Assert.Equal(46, y);
    }

    [Fact]
    public void FromCaretRect_Supports_Negative_Monitor_Coordinates()
    {
        var rect = new NativeMethods.RECT { Left = -200, Top = 20, Right = -199, Bottom = 40 };

        (int x, int y) = MarkerPosition.FromCaretRect(rect);

        Assert.Equal(-193, x);
        Assert.Equal(46, y);
    }

    [Theory]
    [InlineData(0, 0, 1, 20, 6, 7, 26)]
    [InlineData(100, 100, 102, 130, 12, 114, 142)]
    public void FromCaretRect_Uses_Provided_Offset(int left, int top, int right, int bottom, int offset, int expectedX, int expectedY)
    {
        var rect = new NativeMethods.RECT { Left = left, Top = top, Right = right, Bottom = bottom };

        (int x, int y) = MarkerPosition.FromCaretRect(rect, offset);

        Assert.Equal(expectedX, x);
        Assert.Equal(expectedY, y);
    }

    [Fact]
    public void FromCaretRect_Anchors_To_Exact_Caret_Right_Edge()
    {
        var rect = new NativeMethods.RECT { Left = 420, Top = 200, Right = 421, Bottom = 222 };

        (int x, int y) = MarkerPosition.FromCaretRect(rect, offsetX: 6, offsetY: 8);

        Assert.Equal(427, x);
        Assert.Equal(230, y);
    }
}
