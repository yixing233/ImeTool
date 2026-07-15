using System.IO;
using System.Text.Json;
using ImeTool.Diagnostics;

namespace ImeTool.State;

public sealed record PersistedWindowMemoryEntry(
    string ProcessName,
    string Title,
    bool Enabled,
    bool? IsImeOpen,
    DateTimeOffset UpdatedAt);

public interface IWindowMemoryPersistenceStore
{
    string StoragePath { get; }
    IReadOnlyList<PersistedWindowMemoryEntry> Load();
    bool TrySave(IReadOnlyCollection<PersistedWindowMemoryEntry> entries, out string? error);
}

public sealed class WindowMemoryPersistenceStore : IWindowMemoryPersistenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public WindowMemoryPersistenceStore(string? storagePath = null)
    {
        StoragePath = ResolvePath(storagePath);
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ImeTool",
        "window-memory.json");

    public string StoragePath { get; }

    public static string ResolvePath(string? path)
    {
        string candidate = string.IsNullOrWhiteSpace(path)
            ? DefaultPath
            : Environment.ExpandEnvironmentVariables(path.Trim());
        return Path.GetFullPath(candidate);
    }

    public IReadOnlyList<PersistedWindowMemoryEntry> Load()
    {
        try
        {
            if (!File.Exists(StoragePath))
            {
                return [];
            }

            string json = File.ReadAllText(StoragePath);
            PersistedWindowMemoryDocument? document = JsonSerializer.Deserialize<PersistedWindowMemoryDocument>(json, JsonOptions);
            return document?.Entries?
                .Where(IsValid)
                .OrderByDescending(entry => entry.UpdatedAt)
                .ToArray() ?? [];
        }
        catch (Exception exception)
        {
            DiagnosticsLog.Write($"Window memory persistence load failed: path={StoragePath}, error={exception.Message}");
            return [];
        }
    }

    public bool TrySave(IReadOnlyCollection<PersistedWindowMemoryEntry> entries, out string? error)
    {
        error = null;
        string? directory = Path.GetDirectoryName(StoragePath);
        string temporaryPath = StoragePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (Directory.Exists(StoragePath))
            {
                error = "存储路径不能是文件夹";
                return false;
            }

            if (File.Exists(StoragePath))
            {
                using FileStream _ = File.Open(StoragePath, FileMode.Open, FileAccess.Write, FileShare.Read);
            }

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var document = new PersistedWindowMemoryDocument
            {
                Version = 1,
                Entries = entries.Where(IsValid).OrderByDescending(entry => entry.UpdatedAt).ToArray()
            };
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
            File.Move(temporaryPath, StoragePath, overwrite: true);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            DiagnosticsLog.Write($"Window memory persistence save failed: path={StoragePath}, error={exception.Message}");
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
            }
        }
    }

    public bool TryProbeWrite(out string? error)
    {
        error = null;
        string? directory = Path.GetDirectoryName(StoragePath);
        string probePath = Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? Environment.CurrentDirectory : directory,
            ".imetool-write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            if (Directory.Exists(StoragePath))
            {
                error = "存储路径不能是文件夹";
                return false;
            }

            if (File.Exists(StoragePath))
            {
                using FileStream _ = File.Open(StoragePath, FileMode.Open, FileAccess.Write, FileShare.Read);
            }

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(probePath, string.Empty);
            File.Delete(probePath);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(probePath))
                {
                    File.Delete(probePath);
                }
            }
            catch
            {
            }
        }
    }

    private static bool IsValid(PersistedWindowMemoryEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.ProcessName) &&
        !string.IsNullOrWhiteSpace(entry.Title);

    private sealed record PersistedWindowMemoryDocument
    {
        public int Version { get; init; } = 1;
        public IReadOnlyList<PersistedWindowMemoryEntry> Entries { get; init; } = [];
    }
}
