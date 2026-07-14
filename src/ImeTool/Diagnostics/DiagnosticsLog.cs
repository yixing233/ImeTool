using System.IO;
using System.Collections.Concurrent;
using System.Text;

namespace ImeTool.Diagnostics;

public static class DiagnosticsLog
{
    private static readonly object LockObject = new();
    private static readonly Dictionary<string, DateTimeOffset> LastWritesByKey = new(StringComparer.Ordinal);
    private static readonly BlockingCollection<string> PendingLines = new(
        new ConcurrentQueue<string>(),
        boundedCapacity: 2048);

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ImeTool",
        "diagnostics.log");

    static DiagnosticsLog()
    {
        _ = Task.Factory.StartNew(
            ProcessPendingLines,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public static void WriteThrottled(string message) => WriteThrottled(message, message);

    public static void WriteThrottled(string message, string throttleKey)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        lock (LockObject)
        {
            if (LastWritesByKey.TryGetValue(throttleKey, out DateTimeOffset lastWrite) &&
                now - lastWrite < TimeSpan.FromSeconds(2))
            {
                return;
            }

            LastWritesByKey[throttleKey] = now;
            if (LastWritesByKey.Count > 256)
            {
                string[] staleKeys = LastWritesByKey
                    .Where(pair => now - pair.Value > TimeSpan.FromMinutes(1))
                    .Select(pair => pair.Key)
                    .ToArray();
                foreach (string staleKey in staleKeys)
                {
                    LastWritesByKey.Remove(staleKey);
                }
            }

            Write(message);
        }
    }

    public static void Write(string message)
    {
        PendingLines.TryAdd(
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}{Environment.NewLine}");
    }

    private static void ProcessPendingLines()
    {
        try
        {
            string? directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            RotateOversizedLog();
            foreach (string firstLine in PendingLines.GetConsumingEnumerable())
            {
                var batch = new StringBuilder(firstLine);
                while (batch.Length < 64 * 1024 && PendingLines.TryTake(out string? nextLine))
                {
                    batch.Append(nextLine);
                }

                try
                {
                    File.AppendAllText(LogPath, batch.ToString());
                }
                catch
                {
                    // Diagnostics must never affect marker behavior.
                }
            }
        }
        catch
        {
            // Diagnostics must never affect marker behavior.
        }
    }

    private static void RotateOversizedLog()
    {
        try
        {
            var file = new FileInfo(LogPath);
            if (!file.Exists || file.Length <= 8L * 1024 * 1024)
            {
                return;
            }

            string previousPath = LogPath + ".old";
            File.Move(LogPath, previousPath, overwrite: true);
        }
        catch
        {
            // Rotation failure must not disable diagnostics.
        }
    }
}
