using ImeTool.Caret;
using ImeTool.Native;

namespace ImeTool.Tests.Caret;

public sealed class CaretSnapshotStabilizerTests
{
    [Fact]
    public void Same_Source_Tracks_Immediately()
    {
        var stabilizer = new CaretSnapshotStabilizer();
        stabilizer.Stabilize(Snapshot(100, 200, CaretSource.GuiThreadInfo));

        CaretSnapshot result = stabilizer.Stabilize(Snapshot(130, 200, CaretSource.GuiThreadInfo));

        Assert.Equal(130, result.ScreenRect.Left);
    }

    [Fact]
    public void One_Frame_Distant_Source_Change_Is_Ignored()
    {
        var stabilizer = new CaretSnapshotStabilizer();
        stabilizer.Stabilize(Snapshot(100, 200, CaretSource.GuiThreadInfo));

        CaretSnapshot result = stabilizer.Stabilize(Snapshot(500, 600, CaretSource.UiAutomationTextPattern));

        Assert.Equal(CaretSource.GuiThreadInfo, result.Source);
        Assert.Equal(100, result.ScreenRect.Left);
    }

    [Fact]
    public void Repeated_New_Source_Is_Accepted()
    {
        var stabilizer = new CaretSnapshotStabilizer();
        stabilizer.Stabilize(Snapshot(100, 200, CaretSource.GuiThreadInfo));
        stabilizer.Stabilize(Snapshot(500, 600, CaretSource.UiAutomationTextPattern));

        CaretSnapshot result = stabilizer.Stabilize(Snapshot(510, 600, CaretSource.UiAutomationTextPattern));

        Assert.Equal(CaretSource.UiAutomationTextPattern, result.Source);
        Assert.Equal(510, result.ScreenRect.Left);
    }

    [Fact]
    public void Focus_Change_Is_Never_Delayed()
    {
        var stabilizer = new CaretSnapshotStabilizer();
        stabilizer.Stabilize(Snapshot(100, 200, CaretSource.GuiThreadInfo, focus: 1));

        CaretSnapshot result = stabilizer.Stabilize(
            Snapshot(500, 600, CaretSource.UiAutomationTextPattern, focus: 2));

        Assert.Equal(new IntPtr(2), result.FocusHwnd);
        Assert.Equal(500, result.ScreenRect.Left);
    }

    private static CaretSnapshot Snapshot(
        int left,
        int bottom,
        CaretSource source,
        int focus = 1) => new(
        new IntPtr(focus),
        new IntPtr(focus),
        new NativeMethods.RECT
        {
            Left = left,
            Top = bottom - 20,
            Right = left + 1,
            Bottom = bottom
        },
        source);
}
