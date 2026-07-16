using ImeTool.Native;
using ImeTool.Overlay;

namespace ImeTool.Tests.Overlay;

public sealed class WindowBorderGeometryTests
{
    [Fact]
    public void TryNormalize_PreservesValidMultiMonitorCoordinates()
    {
        var rect = new NativeMethods.RECT
        {
            Left = -1920,
            Top = -120,
            Right = 0,
            Bottom = 960
        };

        Assert.True(WindowBorderGeometry.TryNormalize(rect, out NativeMethods.RECT result));
        Assert.Equal(-1920, result.Left);
        Assert.Equal(-120, result.Top);
        Assert.Equal(1920, result.Width);
        Assert.Equal(1080, result.Height);
    }

    [Theory]
    [InlineData(0, 0, 0, 100)]
    [InlineData(0, 0, 100, 0)]
    [InlineData(100, 0, 20, 100)]
    [InlineData(0, 100, 100, 20)]
    public void TryNormalize_RejectsEmptyOrInvertedBounds(
        int left,
        int top,
        int right,
        int bottom)
    {
        var rect = new NativeMethods.RECT
        {
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom
        };

        Assert.False(WindowBorderGeometry.TryNormalize(rect, out _));
    }
}
