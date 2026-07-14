namespace ImeTool;

public static class StartupLaunchPolicy
{
    public static bool ShouldShowSettings(IEnumerable<string> arguments, bool silentStart)
    {
        string[] args = arguments.ToArray();
        bool settingsRequested = HasArgument(args, "--settings");
        bool trayPreviewRequested = HasArgument(args, "--tray-menu");
        return settingsRequested || (!trayPreviewRequested && !silentStart);
    }

    private static bool HasArgument(IEnumerable<string> arguments, string expected) =>
        arguments.Any(argument => string.Equals(argument, expected, StringComparison.OrdinalIgnoreCase));
}
