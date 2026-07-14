using ImeTool.Overlay;

namespace ImeTool.Tests.Overlay;

public sealed class MarkerMotionTests
{
    [Fact]
    public void Nearby_Caret_Movement_Is_Animated()
    {
        Assert.True(MarkerMotion.ShouldAnimate(100, 100, 118, 104));
    }

    [Fact]
    public void Large_Window_Jump_Snaps_Without_Flying_Across_Screen()
    {
        Assert.False(MarkerMotion.ShouldAnimate(100, 100, 900, 700));
    }

    [Fact]
    public void Cubic_Easing_Has_Stable_Endpoints()
    {
        Assert.Equal(0, MarkerMotion.EaseOutCubic(-1));
        Assert.Equal(0.875, MarkerMotion.EaseOutCubic(0.5), precision: 3);
        Assert.Equal(1, MarkerMotion.EaseOutCubic(2));
    }

    [Fact]
    public void Duration_Is_Bounded_For_Responsiveness()
    {
        Assert.InRange(MarkerMotion.DurationMilliseconds(1), 82, 125);
        Assert.InRange(MarkerMotion.DurationMilliseconds(400), 82, 125);
    }

    [Fact]
    public void Configured_Duration_Changes_Follow_Speed_And_Remains_Bounded()
    {
        double fast = MarkerMotion.DurationMilliseconds(100, 50);
        double slow = MarkerMotion.DurationMilliseconds(100, 240);

        Assert.True(fast < slow);
        Assert.InRange(fast, 40, 360);
        Assert.InRange(slow, 40, 360);
    }

    [Theory]
    [InlineData(500, 503, 4, 500)]
    [InlineData(500, 496, 4, 500)]
    [InlineData(500, 506, 4, 506)]
    public void Small_Vertical_Provider_Jitter_Is_Stabilized(
        double previous,
        double candidate,
        double tolerance,
        double expected)
    {
        Assert.Equal(expected, MarkerMotion.StabilizeTarget(previous, candidate, tolerance));
    }
}
