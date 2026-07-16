using ImeTool.Caret;
using ImeTool.Native;

namespace ImeTool.Tests.Caret;

public sealed class CaretCaptureEnvironmentTests
{
    [Theory]
    [InlineData("chrome.exe", "Chrome_WidgetWin_1", CaretTargetEnvironment.ChromiumBrowser)]
    [InlineData("msedge", "Chrome_WidgetWin_1", CaretTargetEnvironment.ChromiumBrowser)]
    [InlineData("firefox.exe", "MozillaWindowClass", CaretTargetEnvironment.FirefoxBrowser)]
    [InlineData("idea64.exe", "SunAwtFrame", CaretTargetEnvironment.Java)]
    [InlineData("notepad.exe", "Notepad", CaretTargetEnvironment.Standard)]
    public void Classifies_Common_Targets(
        string processName,
        string windowClass,
        CaretTargetEnvironment expected)
    {
        Assert.Equal(expected, CaretCaptureEnvironmentClassifier.Classify(processName, windowClass));
    }

    [Fact]
    public void Chromium_Compatibility_Prefers_Uia()
    {
        Assert.True(BrowserCaretCompatibilityPolicy.TrySelect(
            CaretTargetEnvironment.ChromiumBrowser,
            Snapshot(10, CaretSource.UiAutomationTextPattern),
            Snapshot(20, CaretSource.Msaa),
            Snapshot(30, CaretSource.GuiThreadInfo),
            out CaretSnapshot selected));

        Assert.Equal(10, selected.ScreenRect.Left);
        Assert.Equal(CaretSource.BrowserUiAutomation, selected.Source);
    }

    [Fact]
    public void Firefox_Compatibility_Prefers_Msaa()
    {
        Assert.True(BrowserCaretCompatibilityPolicy.TrySelect(
            CaretTargetEnvironment.FirefoxBrowser,
            Snapshot(10, CaretSource.UiAutomationTextPattern),
            Snapshot(20, CaretSource.Msaa),
            Snapshot(30, CaretSource.GuiThreadInfo),
            out CaretSnapshot selected));

        Assert.Equal(20, selected.ScreenRect.Left);
        Assert.Equal(CaretSource.BrowserMsaa, selected.Source);
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
