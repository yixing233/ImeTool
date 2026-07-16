using ImeTool.Caret;
using ImeTool.Native;

namespace ImeTool.Tests.Caret;

public sealed class CaretCaptureModePolicyTests
{
    [Fact]
    public void Automatic_Uses_Validated_Native_Caret_For_Standard_Window()
    {
        CaretSnapshot selected = Select(
            CaretCaptureMode.Automatic,
            CaretTargetEnvironment.Standard,
            native: Snapshot(10, CaretSource.GuiThreadInfo),
            trustedNative: Snapshot(20, CaretSource.GuiThreadInfo),
            automation: Snapshot(30, CaretSource.UiAutomationTextPattern),
            msaa: Snapshot(40, CaretSource.Msaa));

        Assert.Equal(20, selected.ScreenRect.Left);
    }

    [Fact]
    public void Automatic_Does_Not_Use_Unvalidated_Native_Caret()
    {
        CaretSnapshot selected = Select(
            CaretCaptureMode.Automatic,
            CaretTargetEnvironment.Standard,
            native: Snapshot(10, CaretSource.GuiThreadInfo),
            automation: Snapshot(30, CaretSource.UiAutomationTextPattern));

        Assert.Equal(CaretSource.UiAutomationTextPattern, selected.Source);
    }

    [Fact]
    public void Automatic_Uses_Browser_Compatibility_Result_For_Browser()
    {
        CaretSnapshot selected = Select(
            CaretCaptureMode.Automatic,
            CaretTargetEnvironment.ChromiumBrowser,
            automation: Snapshot(30, CaretSource.UiAutomationTextPattern),
            browser: Snapshot(50, CaretSource.BrowserUiAutomation));

        Assert.Equal(CaretSource.BrowserUiAutomation, selected.Source);
    }

    [Fact]
    public void Automatic_Uses_Jab_First_For_Java_Window()
    {
        CaretSnapshot selected = Select(
            CaretCaptureMode.Automatic,
            CaretTargetEnvironment.Java,
            automation: Snapshot(30, CaretSource.UiAutomationTextPattern),
            jab: Snapshot(60, CaretSource.JavaAccessBridge));

        Assert.Equal(CaretSource.JavaAccessBridge, selected.Source);
    }

    [Theory]
    [InlineData(CaretCaptureMode.Win32, CaretSource.GuiThreadInfo)]
    [InlineData(CaretCaptureMode.UiAutomation, CaretSource.UiAutomationTextPattern)]
    [InlineData(CaretCaptureMode.Msaa, CaretSource.Msaa)]
    [InlineData(CaretCaptureMode.BrowserCompatibility, CaretSource.BrowserUiAutomation)]
    [InlineData(CaretCaptureMode.JavaAccessBridge, CaretSource.JavaAccessBridge)]
    public void Explicit_Mode_Uses_Only_Its_Selected_Reader(
        CaretCaptureMode mode,
        CaretSource expectedSource)
    {
        CaretSnapshot selected = Select(
            mode,
            CaretTargetEnvironment.Standard,
            native: Snapshot(10, CaretSource.GuiThreadInfo),
            trustedNative: Snapshot(20, CaretSource.GuiThreadInfo),
            automation: Snapshot(30, CaretSource.UiAutomationTextPattern),
            msaa: Snapshot(40, CaretSource.Msaa),
            browser: Snapshot(50, CaretSource.BrowserUiAutomation),
            jab: Snapshot(60, CaretSource.JavaAccessBridge));

        Assert.Equal(expectedSource, selected.Source);
    }

    [Fact]
    public void Invalid_Mode_Normalizes_To_Automatic()
    {
        Assert.Equal(
            CaretCaptureMode.Automatic,
            CaretCaptureModePolicy.Normalize((CaretCaptureMode)99));
    }

    private static CaretSnapshot Select(
        CaretCaptureMode mode,
        CaretTargetEnvironment environment,
        CaretSnapshot? native = null,
        CaretSnapshot? trustedNative = null,
        CaretSnapshot? automation = null,
        CaretSnapshot? msaa = null,
        CaretSnapshot? browser = null,
        CaretSnapshot? jab = null)
    {
        Assert.True(CaretCaptureModePolicy.TrySelect(
            mode,
            environment,
            native,
            trustedNative,
            automation,
            msaa,
            browser,
            jab,
            out CaretSnapshot selected));
        return selected;
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
