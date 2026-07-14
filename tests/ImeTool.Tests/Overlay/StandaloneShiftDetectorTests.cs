using ImeTool.Overlay;

namespace ImeTool.Tests.Overlay;

public sealed class StandaloneShiftDetectorTests
{
    [Fact]
    public void Quick_Standalone_Shift_Is_Detected()
    {
        var detector = new StandaloneShiftDetector();

        Assert.False(detector.Process(StandaloneShiftDetector.VkLeftShift, true, 100));
        Assert.True(detector.Process(StandaloneShiftDetector.VkLeftShift, false, 180));
    }

    [Fact]
    public void Shift_Used_With_Another_Key_Is_Not_A_Mode_Toggle()
    {
        var detector = new StandaloneShiftDetector();

        detector.Process(StandaloneShiftDetector.VkLeftShift, true, 100);
        detector.Process(0x41, true, 130);
        detector.Process(0x41, false, 150);

        Assert.False(detector.Process(StandaloneShiftDetector.VkLeftShift, false, 180));
    }

    [Fact]
    public void Shift_Pressed_While_Control_Is_Held_Is_Not_A_Mode_Toggle()
    {
        var detector = new StandaloneShiftDetector();
        detector.Process(0x11, true, 50);
        detector.Process(StandaloneShiftDetector.VkRightShift, true, 100);

        Assert.False(detector.Process(StandaloneShiftDetector.VkRightShift, false, 160));
    }

    [Fact]
    public void Long_Shift_Hold_Is_Not_A_Mode_Toggle()
    {
        var detector = new StandaloneShiftDetector();
        detector.Process(StandaloneShiftDetector.VkShift, true, 100);

        Assert.False(detector.Process(
            StandaloneShiftDetector.VkShift,
            false,
            100 + StandaloneShiftDetector.MaximumTapDurationMilliseconds + 1));
    }
}
