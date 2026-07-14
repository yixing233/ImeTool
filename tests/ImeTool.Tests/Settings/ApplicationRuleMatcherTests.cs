using ImeTool.Settings;

namespace ImeTool.Tests.Settings;

public sealed class ApplicationRuleMatcherTests
{
    [Fact]
    public void Matches_Case_Insensitively_And_Ignores_Exe_Extension()
    {
        var matcher = new ApplicationRuleMatcher(
        [
            new ApplicationRule { ProcessName = "Chrome.EXE", Excluded = true }
        ]);

        ApplicationRuleMatch match = matcher.Match("chrome");

        Assert.True(match.Excluded);
        Assert.False(match.DisableStateRestore);
    }

    [Fact]
    public void Duplicate_Rules_Are_Merged()
    {
        var matcher = new ApplicationRuleMatcher(
        [
            new ApplicationRule { ProcessName = "Code", Excluded = true },
            new ApplicationRule { ProcessName = "code.exe", DisableStateRestore = true }
        ]);

        ApplicationRuleMatch match = matcher.Match("CODE.EXE");

        Assert.True(match.Excluded);
        Assert.True(match.DisableStateRestore);
    }

    [Fact]
    public void Unknown_Process_Has_No_Rule()
    {
        var matcher = new ApplicationRuleMatcher(
        [
            new ApplicationRule { ProcessName = "notepad", Excluded = true }
        ]);

        Assert.Equal(ApplicationRuleMatch.None, matcher.Match("wordpad"));
    }
}
