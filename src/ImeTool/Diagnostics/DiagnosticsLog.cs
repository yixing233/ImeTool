using System.Collections.Concurrent;
using System.IO;
using System.Text;
using ImeTool.Settings;

namespace ImeTool.Diagnostics;

public static class DiagnosticsLog
{
    private const long MaximumLogBytes = 8L * 1024 * 1024;
    private static readonly object ConfigurationLock = new();
    private static readonly object ThrottleLock = new();
    private static readonly Dictionary<string, DateTimeOffset> LastWritesByKey = new(StringComparer.Ordinal);
    private static readonly BlockingCollection<PendingLogItem> PendingItems = new(
        new ConcurrentQueue<PendingLogItem>(),
        boundedCapacity: 2048);
    private static string _logPath = StoragePathService.GetLogPath(StoragePathService.DefaultDirectory);
    private static DiagnosticsLogLevel _minimumLevel = DiagnosticsLogLevel.Warn;

    static DiagnosticsLog()
    {
        _ = Task.Factory.StartNew(
            ProcessPendingItems,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public static string LogPath
    {
        get
        {
            lock (ConfigurationLock)
            {
                return _logPath;
            }
        }
    }

    public static DiagnosticsLogLevel MinimumLevel
    {
        get
        {
            lock (ConfigurationLock)
            {
                return _minimumLevel;
            }
        }
    }

    public static void Configure(string? storageDirectory, DiagnosticsLogLevel minimumLevel)
    {
        string path = StoragePathService.GetLogPath(storageDirectory);
        lock (ConfigurationLock)
        {
            _logPath = path;
            _minimumLevel = DiagnosticsLogLevelPolicy.Normalize(minimumLevel);
        }
    }

    // Backward-compatible calls are informational and are captured when the
    // user selects the Info level.
    public static void Write(string message) => Info(message);

    public static void Info(string message) => Write(DiagnosticsLogLevel.Info, message);

    public static void Warn(string message) => Write(DiagnosticsLogLevel.Warn, message);

    public static void Error(string message) => Write(DiagnosticsLogLevel.Error, message);

    public static void Write(DiagnosticsLogLevel level, string message)
    {
        string path;
        DiagnosticsLogLevel minimumLevel;
        lock (ConfigurationLock)
        {
            path = _logPath;
            minimumLevel = _minimumLevel;
        }

        level = DiagnosticsLogLevelPolicy.Normalize(level);
        if (!DiagnosticsLogLevelPolicy.ShouldCapture(minimumLevel, level))
        {
            return;
        }

        string line = FormatLine(DateTimeOffset.Now, level, message);
        PendingItems.TryAdd(new PendingLogItem(path, line, null));
    }

    public static void WriteThrottled(string message) => WriteThrottled(message, message);

    public static void WriteThrottled(string message, string throttleKey) =>
        WriteThrottled(DiagnosticsLogLevel.Info, message, throttleKey);

    public static void WarnThrottled(string message, string throttleKey) =>
        WriteThrottled(DiagnosticsLogLevel.Warn, message, throttleKey);

    public static void WriteThrottled(
        DiagnosticsLogLevel level,
        string message,
        string throttleKey)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        lock (ThrottleLock)
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
        }

        Write(level, message);
    }

    public static async Task FlushAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!PendingItems.TryAdd(new PendingLogItem(null, null, completion)))
        {
            return;
        }

        await completion.Task.ConfigureAwait(false);
    }

    internal static string FormatLine(
        DateTimeOffset timestamp,
        DiagnosticsLogLevel level,
        string message) =>
        $"{timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{DiagnosticsLogLevelPolicy.Label(level)}] {message}{Environment.NewLine}";

    private static void ProcessPendingItems()
    {
        foreach (PendingLogItem firstItem in PendingItems.GetConsumingEnumerable())
        {
            try
            {
                ProcessBatch(firstItem);
            }
            catch
            {
                firstItem.Completion?.TrySetResult();
                // Diagnostics must never affect marker behavior.
            }
        }
    }

    private static void ProcessBatch(PendingLogItem firstItem)
    {
        var items = new List<PendingLogItem>(128) { firstItem };
        while (items.Count < 128 && PendingItems.TryTake(out PendingLogItem? nextItem))
        {
            items.Add(nextItem);
        }

        foreach (IGrouping<string, PendingLogItem> group in items
                     .Where(item => item.Path is not null && item.Line is not null)
                     .GroupBy(item => item.Path!, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string? directory = Path.GetDirectoryName(group.Key);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                RotateOversizedLog(group.Key);
                var batch = new StringBuilder();
                foreach (PendingLogItem item in group)
                {
                    batch.Append(item.Line);
                }

                File.AppendAllText(group.Key, batch.ToString());
            }
            catch
            {
                // A read-only or temporarily unavailable data directory must
                // not interrupt input-state tracking.
            }
        }

        foreach (PendingLogItem item in items)
        {
            item.Completion?.TrySetResult();
        }
    }

    private static void RotateOversizedLog(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= MaximumLogBytes)
        {
            return;
        }

        File.Move(path, path + ".old", overwrite: true);
    }

    private sealed record PendingLogItem(
        string? Path,
        string? Line,
        TaskCompletionSource? Completion);
}
