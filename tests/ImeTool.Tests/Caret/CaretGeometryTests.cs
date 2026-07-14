using ImeTool.Caret;

namespace ImeTool.Tests.Caret;

public sealed class CaretGeometryTests
{
    [Fact]
    public void ExactNarrowRectangle_IsAccepted()
    {
        bool accepted = CaretGeometry.TryCreateExactRect(
            new System.Windows.Rect(500, 300, 1, 22),
            out var rect);

        Assert.True(accepted);
        Assert.Equal(500, rect.Left);
        Assert.Equal(501, rect.Right);
    }

    [Fact]
    public void WideSelectionRectangle_IsRejected()
    {
        bool accepted = CaretGeometry.TryCreateExactRect(
            new System.Windows.Rect(357, 579, 167, 20),
            out _);

        Assert.False(accepted);
    }

    [Fact]
    public void NonFiniteRectangle_IsRejected()
    {
        bool accepted = CaretGeometry.TryCreateExactRect(
            new System.Windows.Rect(10, 20, double.PositiveInfinity, 20),
            out _);

        Assert.False(accepted);
    }
}
