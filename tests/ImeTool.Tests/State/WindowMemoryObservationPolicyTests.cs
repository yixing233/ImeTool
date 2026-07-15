using ImeTool.State;

namespace ImeTool.Tests.State;

public sealed class WindowMemoryObservationPolicyTests
{
    [Fact]
    public void Valid_External_Text_Input_Window_Is_Observed()
    {
        Assert.True(WindowMemoryObservationPolicy.ShouldObserve(
            hasValidatedTextCaret: true,
            hasWindowKey: true,
            isOwnProcess: false,
            isVisible: true,
            isMinimized: false,
            isExcluded: false));
    }

    [Theory]
    [InlineData(false, true, false, true, false, false)]
    [InlineData(true, false, false, true, false, false)]
    [InlineData(true, true, true, true, false, false)]
    [InlineData(true, true, false, false, false, false)]
    [InlineData(true, true, false, true, true, false)]
    [InlineData(true, true, false, true, false, true)]
    public void Non_Input_Or_Ineligible_Window_Is_Not_Observed(
        bool hasValidatedTextCaret,
        bool hasWindowKey,
        bool isOwnProcess,
        bool isVisible,
        bool isMinimized,
        bool isExcluded)
    {
        Assert.False(WindowMemoryObservationPolicy.ShouldObserve(
            hasValidatedTextCaret,
            hasWindowKey,
            isOwnProcess,
            isVisible,
            isMinimized,
            isExcluded));
    }
}
