using ImeTool.Native;
using ImeTool.Overlay;

namespace ImeTool.Tests.Overlay;

public sealed class MarkerPlacementTests
{
    private static readonly NativeMethods.RECT WorkArea = new()
    {
        Left = 0,
        Top = 0,
        Right = 1920,
        Bottom = 1040
    };

    [Fact]
    public void Normal_Caret_Uses_Right_Below_Placement()
    {
        MarkerPlacementResult result = MarkerPlacement.Calculate(
            Caret(500, 400), 48, 36, 6, 6, 6, WorkArea);

        Assert.Equal(501, result.Left);
        Assert.Equal(420, result.Top);
        Assert.False(result.FlippedHorizontal);
        Assert.False(result.FlippedVertical);
    }

    [Fact]
    public void Right_Edge_Clamps_Without_Flipping_To_Opposite_Side()
    {
        MarkerPlacementResult result = MarkerPlacement.Calculate(
            Caret(1900, 400), 60, 36, 6, 6, 6, WorkArea);

        Assert.Equal(1860, result.Left);
        Assert.False(result.FlippedHorizontal);
    }

    [Fact]
    public void Bottom_Edge_Clamps_Without_Flipping_Above_Caret()
    {
        MarkerPlacementResult result = MarkerPlacement.Calculate(
            Caret(500, 1020), 48, 40, 6, 6, 6, WorkArea);

        Assert.Equal(1000, result.Top);
        Assert.False(result.FlippedVertical);
    }

    [Fact]
    public void Negative_Monitor_Coordinates_Are_Clamped_To_Work_Area()
    {
        var workArea = new NativeMethods.RECT
        {
            Left = -1920,
            Top = -200,
            Right = 0,
            Bottom = 880
        };

        MarkerPlacementResult result = MarkerPlacement.Calculate(
            Caret(-1930, -210), 80, 50, 6, 6, 6, workArea);

        Assert.Equal(-1920, result.Left);
        Assert.True(result.Top >= -200);
    }

    [Fact]
    public void Oversized_Marker_Remains_Anchored_To_Work_Area_Origin()
    {
        var workArea = new NativeMethods.RECT { Left = 100, Top = 200, Right = 140, Bottom = 230 };

        MarkerPlacementResult result = MarkerPlacement.Calculate(
            Caret(120, 210), 100, 80, 6, 6, 6, workArea);

        Assert.Equal(100, result.Left);
        Assert.Equal(200, result.Top);
    }

    private static NativeMethods.RECT Caret(int left, int top) => new()
    {
        Left = left,
        Top = top,
        Right = left + 1,
        Bottom = top + 20
    };
}
