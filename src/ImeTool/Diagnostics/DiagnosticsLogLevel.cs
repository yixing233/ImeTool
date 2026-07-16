namespace ImeTool.Diagnostics;

public enum DiagnosticsLogLevel
{
    Info = 0,
    Warn = 1,
    Error = 2
}

public static class DiagnosticsLogLevelPolicy
{
    public static DiagnosticsLogLevel Normalize(DiagnosticsLogLevel level) =>
        Enum.IsDefined(level) ? level : DiagnosticsLogLevel.Warn;

    public static bool ShouldCapture(
        DiagnosticsLogLevel minimumLevel,
        DiagnosticsLogLevel messageLevel) =>
        Normalize(messageLevel) >= Normalize(minimumLevel);

    public static string Label(DiagnosticsLogLevel level) => Normalize(level) switch
    {
        DiagnosticsLogLevel.Info => "INFO",
        DiagnosticsLogLevel.Error => "ERROR",
        _ => "WARN"
    };
}
