using ImeTool.Overlay;

namespace ImeTool.Tests.Overlay;

public sealed class MouseMarkerPolicyTests
{
    [Fact]
    public void ShouldShow_WhenCursorIsVisibleSystemIBeamWithoutCaret()
    {
        bool result = MouseMarkerPolicy.ShouldShow(
            enabled: true,
            hasTextCaret: false,
            cursorReadSucceeded: true,
            cursorVisible: true,
            isSystemIBeam: true,
            isOwnProcess: false,
            MarkerState.Chinese);

        Assert.True(result);
    }

    [Theory]
    [InlineData(false, false, true, true, true, false, MarkerState.Chinese)]
    [InlineData(true, true, true, true, true, false, MarkerState.Chinese)]
    [InlineData(true, false, false, true, true, false, MarkerState.Chinese)]
    [InlineData(true, false, true, false, true, false, MarkerState.Chinese)]
    [InlineData(true, false, true, true, false, false, MarkerState.Chinese)]
    [InlineData(true, false, true, true, true, true, MarkerState.Chinese)]
    [InlineData(true, false, true, true, true, false, MarkerState.Unknown)]
    public void ShouldShow_RejectsInvalidContexts(
        bool enabled,
        bool hasTextCaret,
        bool cursorReadSucceeded,
        bool cursorVisible,
        bool isSystemIBeam,
        bool isOwnProcess,
        MarkerState state)
    {
        Assert.False(MouseMarkerPolicy.ShouldShow(
            enabled,
            hasTextCaret,
            cursorReadSucceeded,
            cursorVisible,
            isSystemIBeam,
            isOwnProcess,
            state));
    }
}
