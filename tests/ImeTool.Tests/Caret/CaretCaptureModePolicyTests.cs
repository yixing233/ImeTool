using ImeTool.Caret;
using ImeTool.Native;

namespace ImeTool.Tests.Caret;

public sealed class CaretCaptureModePolicyTests
{
    [Fact]
    public void Automatic_Uses_Validated_Native_Caret()
    {
        CaretSnapshot rawNative = Snapshot(10, CaretSource.GuiThreadInfo);
        CaretSnapshot trustedNative = Snapshot(20, CaretSource.GuiThreadInfo);
        CaretSnapshot automation = Snapshot(30, CaretSource.UiAutomationTextPattern);

        bool found = CaretCaptureModePolicy.TrySelect(
            CaretCaptureMode.Automatic,
            rawNative,
            trustedNative,
            automation,
            out CaretSnapshot selected);

        Assert.True(found);
        Assert.Equal(20, selected.ScreenRect.Left);
    }

    [Fact]
    public void Automatic_Does_Not_Use_Unvalidated_Native_Caret()
    {
        bool found = CaretCaptureModePolicy.TrySelect(
            CaretCaptureMode.Automatic,
            Snapshot(10, CaretSource.GuiThreadInfo),
            trustedNativeSnapshot: null,
            Snapshot(30, CaretSource.UiAutomationTextPattern),
            out CaretSnapshot selected);

        Assert.True(found);
        Assert.Equal(CaretSource.UiAutomationTextPattern, selected.Source);
    }

    [Fact]
    public void Win32_Mode_Uses_Raw_Native_Caret()
    {
        bool found = CaretCaptureModePolicy.TrySelect(
            CaretCaptureMode.Win32,
            Snapshot(10, CaretSource.GuiThreadInfo),
            trustedNativeSnapshot: null,
            Snapshot(30, CaretSource.UiAutomationTextPattern),
            out CaretSnapshot selected);

        Assert.True(found);
        Assert.Equal(CaretSource.GuiThreadInfo, selected.Source);
    }

    [Fact]
    public void UiAutomation_Mode_Ignores_Native_Caret()
    {
        bool found = CaretCaptureModePolicy.TrySelect(
            CaretCaptureMode.UiAutomation,
            Snapshot(10, CaretSource.GuiThreadInfo),
            Snapshot(20, CaretSource.GuiThreadInfo),
            Snapshot(30, CaretSource.UiAutomationTextPattern),
            out CaretSnapshot selected);

        Assert.True(found);
        Assert.Equal(CaretSource.UiAutomationTextPattern, selected.Source);
    }

    [Fact]
    public void Invalid_Mode_Normalizes_To_Automatic()
    {
        Assert.Equal(
            CaretCaptureMode.Automatic,
            CaretCaptureModePolicy.Normalize((CaretCaptureMode)99));
    }

    private static CaretSnapshot Snapshot(int left, CaretSource source) => new(
        new IntPtr(1),
        new IntPtr(1),
        new NativeMethods.RECT
        {
            Left = left,
            Top = 10,
            Right = left + 1,
            Bottom = 30
        },
        source);
}
