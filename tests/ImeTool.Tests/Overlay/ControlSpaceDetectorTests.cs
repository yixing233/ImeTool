using ImeTool.Overlay;

namespace ImeTool.Tests.Overlay;

public sealed class ControlSpaceDetectorTests
{
    [Fact]
    public void Control_Space_Is_Detected_On_Space_Release()
    {
        var detector = new ControlSpaceDetector();

        Assert.False(detector.Process(ControlSpaceDetector.VkLeftControl, true));
        Assert.False(detector.Process(ControlSpaceDetector.VkSpace, true));
        Assert.True(detector.Process(ControlSpaceDetector.VkSpace, false));
        Assert.False(detector.Process(ControlSpaceDetector.VkLeftControl, false));
    }

    [Fact]
    public void Space_Without_Control_Is_Not_Detected()
    {
        var detector = new ControlSpaceDetector();

        detector.Process(ControlSpaceDetector.VkSpace, true);

        Assert.False(detector.Process(ControlSpaceDetector.VkSpace, false));
    }

    [Fact]
    public void Another_Key_Cancels_Control_Space()
    {
        var detector = new ControlSpaceDetector();
        detector.Process(ControlSpaceDetector.VkControl, true);
        detector.Process(ControlSpaceDetector.VkSpace, true);
        detector.Process(0x41, true);
        detector.Process(0x41, false);

        Assert.False(detector.Process(ControlSpaceDetector.VkSpace, false));
    }

    [Fact]
    public void Repeated_Space_Keydown_Does_Not_Duplicate_Toggle()
    {
        var detector = new ControlSpaceDetector();
        detector.Process(ControlSpaceDetector.VkRightControl, true);
        detector.Process(ControlSpaceDetector.VkSpace, true);

        Assert.False(detector.Process(ControlSpaceDetector.VkSpace, true));
        Assert.True(detector.Process(ControlSpaceDetector.VkSpace, false));
    }

    [Fact]
    public void Releasing_Control_Before_Space_Cancels_Toggle()
    {
        var detector = new ControlSpaceDetector();
        detector.Process(ControlSpaceDetector.VkControl, true);
        detector.Process(ControlSpaceDetector.VkSpace, true);
        detector.Process(ControlSpaceDetector.VkControl, false);

        Assert.False(detector.Process(ControlSpaceDetector.VkSpace, false));
    }
}
