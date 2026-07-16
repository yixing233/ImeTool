using ImeTool.Caret;
using ImeTool.Native;

namespace ImeTool.Tests.Caret;

public sealed class JavaAccessBridgeGeometryTests
{
    [Fact]
    public void Scales_Jab_Logical_Coordinates_To_Window_Dpi()
    {
        Assert.True(JavaAccessBridgeGeometry.TryCreateRect(
            100,
            200,
            1,
            20,
            144,
            out NativeMethods.RECT rect));

        Assert.Equal(150, rect.Left);
        Assert.Equal(300, rect.Top);
        Assert.Equal(152, rect.Right);
        Assert.Equal(330, rect.Bottom);
    }

    [Fact]
    public void Zero_Dpi_Falls_Back_To_96_Dpi()
    {
        Assert.True(JavaAccessBridgeGeometry.TryCreateRect(
            100,
            200,
            1,
            20,
            0,
            out NativeMethods.RECT rect));

        Assert.Equal(100, rect.Left);
        Assert.Equal(200, rect.Top);
        Assert.Equal(220, rect.Bottom);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(100, 200, 1, 0)]
    [InlineData(100, 200, 1, 600)]
    public void Rejects_Invalid_Geometry(int x, int y, int width, int height)
    {
        Assert.False(JavaAccessBridgeGeometry.TryCreateRect(
            x,
            y,
            width,
            height,
            96,
            out _));
    }
}
