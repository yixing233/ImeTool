namespace ImeTool;

public static class StartupLaunchPolicy
{
    public static bool ShouldShowSettings(IEnumerable<string> arguments, bool silentStart)
    {
        string[] args = arguments.ToArray();
        bool settingsRequested = HasArgument(args, "--settings");
        bool trayPreviewRequested = HasArgument(args, "--tray-menu");
        bool silentRequested = HasArgument(args, "--silent");
        return settingsRequested || (!trayPreviewRequested && !silentRequested && !silentStart);
    }

    private static bool HasArgument(IEnumerable<string> arguments, string expected) =>
        arguments.Any(argument => string.Equals(argument, expected, StringComparison.OrdinalIgnoreCase));

    public static string? GetArgumentValue(IReadOnlyList<string> arguments, string name)
    {
        for (int index = 0; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }
}
