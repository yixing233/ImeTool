using ImeTool.Caret;
using ImeTool.Native;

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

    [Fact]
    public void TinyNativeCaret_IsRejectedEvenWhenTextHostContainsIt()
    {
        var caret = new NativeMethods.RECT
        {
            Left = 500,
            Top = 310,
            Right = 501,
            Bottom = 313
        };
        var textHost = new NativeMethods.RECT
        {
            Left = 480,
            Top = 300,
            Right = 780,
            Bottom = 342
        };

        bool accepted = CaretGeometry.TryNormalizeNativeRect(caret, textHost, out _);

        Assert.False(accepted);
    }

    [Fact]
    public void TinyNativeCaret_WithoutPlausibleTextHost_IsRejected()
    {
        var caret = new NativeMethods.RECT
        {
            Left = 500,
            Top = 310,
            Right = 501,
            Bottom = 313
        };

        bool accepted = CaretGeometry.TryNormalizeNativeRect(caret, null, out _);

        Assert.False(accepted);
    }

    [Fact]
    public void FullHeightNativeCaret_DoesNotRequireTextHostHint()
    {
        var caret = new NativeMethods.RECT
        {
            Left = 500,
            Top = 310,
            Right = 501,
            Bottom = 332
        };

        bool accepted = CaretGeometry.TryNormalizeNativeRect(caret, null, out var normalized);

        Assert.True(accepted);
        Assert.Equal(caret, normalized);
    }

    [Fact]
    public void FullHeightNativeCaret_OutsideTextHost_IsRejected()
    {
        var caret = new NativeMethods.RECT
        {
            Left = 460,
            Top = 310,
            Right = 461,
            Bottom = 332
        };
        var textHost = new NativeMethods.RECT
        {
            Left = 480,
            Top = 300,
            Right = 780,
            Bottom = 342
        };

        bool accepted = CaretGeometry.TryNormalizeNativeRect(caret, textHost, out _);

        Assert.False(accepted);
    }
}
