using ImeTool.Tracking;

namespace ImeTool.Tests.Tracking;

public sealed class WinEventHookTests
{
    [Fact]
    public void Foreground_Target_Is_Reported()
    {
        Assert.True(WinEventHook.IsForegroundEventTarget(new IntPtr(100), new IntPtr(100)));
    }

    [Fact]
    public void Background_Target_Is_Ignored()
    {
        Assert.False(WinEventHook.IsForegroundEventTarget(new IntPtr(100), new IntPtr(200)));
    }

    [Fact]
    public void Missing_Window_Is_Ignored()
    {
        Assert.False(WinEventHook.IsForegroundEventTarget(IntPtr.Zero, new IntPtr(200)));
        Assert.False(WinEventHook.IsForegroundEventTarget(new IntPtr(100), IntPtr.Zero));
    }
}
