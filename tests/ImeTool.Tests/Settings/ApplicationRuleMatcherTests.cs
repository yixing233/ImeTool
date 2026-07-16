using ImeTool.Settings;

namespace ImeTool.Tests.Settings;

public sealed class ApplicationRuleMatcherTests
{
    [Fact]
    public void Legacy_Excluded_Rule_Expands_To_All_Three_Behaviors()
    {
        var matcher = new ApplicationRuleMatcher(
        [
            new ApplicationRule { ProcessName = "Chrome.EXE", Excluded = true }
        ]);

        ApplicationRuleMatch match = matcher.Match("chrome");

        Assert.True(match.HideMarker);
        Assert.True(match.DisableWindowMemory);
        Assert.True(match.DisableStateRestore);
    }

    [Fact]
    public void Matches_Title_And_Classes_Case_Insensitively()
    {
        var matcher = new ApplicationRuleMatcher(
        [
            new ApplicationRule
            {
                ProcessName = "chrome",
                WindowTitleContains = "GitHub",
                WindowClass = "Chrome_WidgetWin_1",
                ControlClass = "Chrome_RenderWidgetHostHWND",
                HideMarker = true
            }
        ]);

        ApplicationRuleMatch match = matcher.Match(new ApplicationRuleContext(
            "CHROME.EXE",
            "ImeTool · GitHub - Google Chrome",
            "chrome_widgetwin_1",
            "chrome_renderwidgethosthwnd"));

        Assert.True(match.HideMarker);
    }

    [Fact]
    public void Nonmatching_Context_Does_Not_Apply()
    {
        var matcher = new ApplicationRuleMatcher(
        [
            new ApplicationRule
            {
                ProcessName = "chrome",
                WindowTitleContains = "GitHub",
                HideMarker = true
            }
        ]);

        ApplicationRuleMatch match = matcher.Match(new ApplicationRuleContext(
            "chrome",
            "New tab",
            "Chrome_WidgetWin_1",
            string.Empty));

        Assert.Equal(ApplicationRuleMatch.None, match);
    }

    [Fact]
    public void Boolean_Behaviors_Are_Merged_Independently()
    {
        var matcher = new ApplicationRuleMatcher(
        [
            new ApplicationRule { ProcessName = "Code", HideMarker = true },
            new ApplicationRule { ProcessName = "code.exe", DisableWindowMemory = true },
            new ApplicationRule { ProcessName = "CODE", DisableStateRestore = true }
        ]);

        ApplicationRuleMatch match = matcher.Match("code");

        Assert.True(match.HideMarker);
        Assert.True(match.DisableWindowMemory);
        Assert.True(match.DisableStateRestore);
    }

    [Fact]
    public void Offsets_Are_Taken_From_Most_Specific_Matching_Rules()
    {
        var matcher = new ApplicationRuleMatcher(
        [
            new ApplicationRule { ProcessName = "telegram", OffsetX = 2, OffsetY = 4 },
            new ApplicationRule
            {
                ProcessName = "telegram",
                WindowClass = "Qt51514QWindowIcon",
                OffsetX = 12
            },
            new ApplicationRule
            {
                ProcessName = "telegram",
                WindowClass = "Qt51514QWindowIcon",
                ControlClass = "InputControl",
                OffsetY = 18
            }
        ]);

        ApplicationRuleMatch match = matcher.Match(new ApplicationRuleContext(
            "telegram",
            "Telegram",
            "Qt51514QWindowIcon",
            "InputControl"));

        Assert.Equal(12, match.OffsetX);
        Assert.Equal(18, match.OffsetY);
    }

    [Fact]
    public void Unknown_Process_Has_No_Rule()
    {
        var matcher = new ApplicationRuleMatcher(
        [
            new ApplicationRule { ProcessName = "notepad", HideMarker = true }
        ]);

        Assert.Equal(ApplicationRuleMatch.None, matcher.Match("wordpad"));
    }
}
