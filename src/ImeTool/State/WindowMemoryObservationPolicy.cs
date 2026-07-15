namespace ImeTool.State;

public static class WindowMemoryObservationPolicy
{
    public static bool ShouldObserve(
        bool hasValidatedTextCaret,
        bool hasWindowKey,
        bool isOwnProcess,
        bool isVisible,
        bool isMinimized,
        bool isExcluded) =>
        hasValidatedTextCaret &&
        hasWindowKey &&
        !isOwnProcess &&
        isVisible &&
        !isMinimized &&
        !isExcluded;
}
