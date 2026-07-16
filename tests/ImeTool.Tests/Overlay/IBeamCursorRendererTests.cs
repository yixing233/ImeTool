using ImeTool.Overlay;
using DrawingColor = System.Drawing.Color;

namespace ImeTool.Tests.Overlay;

public sealed class IBeamCursorRendererTests
{
    [Fact]
    public void Render_CreatesTransparentCanvasWithColoredCenter()
    {
        using var bitmap = IBeamCursorRenderer.Render(DrawingColor.FromArgb(239, 68, 68));

        Assert.Equal(IBeamCursorRenderer.DefaultSize, bitmap.Width);
        Assert.Equal(IBeamCursorRenderer.DefaultSize, bitmap.Height);
        Assert.Equal(0, bitmap.GetPixel(0, 0).A);

        DrawingColor center = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        Assert.True(center.A > 200);
        Assert.True(center.R > center.G);
        Assert.True(center.R > center.B);
    }

    [Theory]
    [InlineData(1, 24)]
    [InlineData(80, 64)]
    public void Render_ClampsRequestedSize(int requested, int expected)
    {
        using var bitmap = IBeamCursorRenderer.Render(DrawingColor.Blue, requested);

        Assert.Equal(expected, bitmap.Width);
        Assert.Equal(expected, bitmap.Height);
    }
}
