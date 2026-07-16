using ImeTool.Ime;
using ImeTool.Settings;

namespace ImeTool.Tests.Ime;

public sealed class ImeDetectionRuleMatcherTests
{
    [Fact]
    public void Exact_Layout_And_Codes_Match()
    {
        var matcher = new ImeDetectionRuleMatcher(
        [
            new ImeDetectionRule
            {
                KeyboardLayout = "0x8040804",
                OpenStatusCode = 1,
                ConversionMode = 1025,
                Result = TextInputMode.Chinese
            }
        ]);

        ImeDetectionRule? rule = matcher.Match("0x0000000008040804", 1, 1025);

        Assert.NotNull(rule);
        Assert.Equal(TextInputMode.Chinese, rule.Result);
    }

    [Fact]
    public void Most_Specific_Matching_Rule_Wins()
    {
        var matcher = new ImeDetectionRuleMatcher(
        [
            new ImeDetectionRule { KeyboardLayout = "*", OpenStatusCode = 1, Result = TextInputMode.English },
            new ImeDetectionRule
            {
                KeyboardLayout = "0x8040804",
                OpenStatusCode = 1,
                ConversionMode = 1025,
                Result = TextInputMode.Chinese
            }
        ]);

        ImeDetectionRule? rule = matcher.Match("0x0000000008040804", 1, 1025);

        Assert.Equal(TextInputMode.Chinese, rule?.Result);
    }

    [Fact]
    public void Different_Codes_Do_Not_Match()
    {
        var matcher = new ImeDetectionRuleMatcher(
        [
            new ImeDetectionRule
            {
                KeyboardLayout = "*",
                OpenStatusCode = 1,
                ConversionMode = 1025,
                Result = TextInputMode.Chinese
            }
        ]);

        Assert.Null(matcher.Match("0x0000000008040804", 1, 0));
    }
}
