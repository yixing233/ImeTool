namespace ImeTool.Overlay;

public static class MouseMarkerPolicy
{
    public static bool ShouldShow(
        bool enabled,
        bool hasTextCaret,
        bool cursorReadSucceeded,
        bool cursorVisible,
        bool isSystemIBeam,
        bool isOwnProcess,
        MarkerState state) =>
        enabled &&
        !hasTextCaret &&
        cursorReadSucceeded &&
        cursorVisible &&
        isSystemIBeam &&
        !isOwnProcess &&
        state != MarkerState.Unknown;
}
