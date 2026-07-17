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

    [Fact]
    public void Chromium_Rejects_Msaa_Caret_When_Focused_Text_Host_Is_Unknown()
    {
        Assert.False(BrowserCaretCompatibilityPolicy.TryNormalizeMsaaCandidate(
            CaretTargetEnvironment.ChromiumBrowser,
            textHostBounds: null,
            Snapshot(400, CaretSource.Msaa),
            out _));
    }

    [Fact]
    public void Browser_Rejects_Msaa_Caret_Outside_Focused_Text_Host()
    {
        var host = new NativeMethods.RECT
        {
            Left = 100,
            Top = 80,
            Right = 720,
            Bottom = 136
        };
        var staleCaret = new CaretSnapshot(
            new IntPtr(1),
            new IntPtr(1),
            new NativeMethods.RECT
            {
                Left = 400,
                Top = 36,
                Right = 401,
                Bottom = 58
            },
            CaretSource.Msaa);

        Assert.False(BrowserCaretCompatibilityPolicy.TryNormalizeMsaaCandidate(
            CaretTargetEnvironment.ChromiumBrowser,
            host,
            staleCaret,
            out _));
    }

    [Fact]
    public void Browser_Accepts_Msaa_Caret_Inside_Focused_Text_Host()
    {
        var host = new NativeMethods.RECT
        {
            Left = 100,
            Top = 80,
            Right = 720,
            Bottom = 136
        };
        var caret = new CaretSnapshot(
            new IntPtr(1),
            new IntPtr(1),
            new NativeMethods.RECT
            {
                Left = 132,
                Top = 94,
                Right = 133,
                Bottom = 120
            },
            CaretSource.Msaa);

        Assert.True(BrowserCaretCompatibilityPolicy.TryNormalizeMsaaCandidate(
            CaretTargetEnvironment.ChromiumBrowser,
            host,
            caret,
            out CaretSnapshot normalized));
        Assert.Equal(95, normalized.ScreenRect.Top);
        Assert.Equal(121, normalized.ScreenRect.Bottom);
    }

    [Fact]
    public void Browser_Normalizes_Vertically_Shifted_Msaa_Caret_That_Overlaps_Host()
    {
        var host = new NativeMethods.RECT
        {
            Left = 79,
            Top = 566,
            Right = 479,
            Bottom = 613
        };
        var shiftedCaret = new CaretSnapshot(
            new IntPtr(1),
            new IntPtr(1),
            new NativeMethods.RECT
            {
                Left = 124,
                Top = 609,
                Right = 125,
                Bottom = 630
            },
            CaretSource.Msaa);

        Assert.True(BrowserCaretCompatibilityPolicy.TryNormalizeMsaaCandidate(
            CaretTargetEnvironment.ChromiumBrowser,
            host,
            shiftedCaret,
            out CaretSnapshot normalized));
        Assert.Equal(124, normalized.ScreenRect.Left);
        Assert.Equal(579, normalized.ScreenRect.Top);
        Assert.Equal(600, normalized.ScreenRect.Bottom);
    }

    [Fact]
    public void Firefox_Can_Use_Msaa_When_Uia_Host_Is_Unavailable()
    {
        Assert.True(BrowserCaretCompatibilityPolicy.TryNormalizeMsaaCandidate(
            CaretTargetEnvironment.FirefoxBrowser,
            textHostBounds: null,
            Snapshot(400, CaretSource.Msaa),
            out CaretSnapshot normalized));
        Assert.Equal(400, normalized.ScreenRect.Left);
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
