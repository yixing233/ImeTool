using System.IO;

namespace ImeTool.Diagnostics;

public static class DiagnosticsLog
{
    private static readonly object LockObject = new();
    private static string? _lastMessage;
    private static DateTimeOffset _lastWrite;

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ImeTool",
        "diagnostics.log");

    public static void WriteThrottled(string message)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        lock (LockObject)
        {
            if (message == _lastMessage && now - _lastWrite < TimeSpan.FromSeconds(2))
            {
                return;
            }

            _lastMessage = message;
            _lastWrite = now;
            Write(message);
        }
    }

    public static void Write(string message)
    {
        try
        {
            string? directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never affect marker behavior.
        }
    }
}
