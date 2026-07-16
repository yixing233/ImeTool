using System.IO;

namespace ImeTool.Settings;

public static class StoragePathService
{
    public const string LogFileName = "ImeTool.log";
    public const string WindowMemoryFileName = "window-memory.json";

    public static string DefaultDirectory => Path.GetFullPath(AppContext.BaseDirectory);

    public static string LegacyWindowMemoryDefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ImeTool",
        WindowMemoryFileName);

    public static string ResolveDirectory(string? path)
    {
        string candidate = string.IsNullOrWhiteSpace(path)
            ? DefaultDirectory
            : Environment.ExpandEnvironmentVariables(path.Trim());
        return Path.GetFullPath(candidate);
    }

    public static string GetLogPath(string? storageDirectory) =>
        Path.Combine(ResolveDirectory(storageDirectory), LogFileName);

    public static string GetWindowMemoryPath(string? storageDirectory) =>
        Path.Combine(ResolveDirectory(storageDirectory), WindowMemoryFileName);

    public static bool TryProbeWrite(string? storageDirectory, out string? error)
    {
        error = null;
        string directory;
        try
        {
            directory = ResolveDirectory(storageDirectory);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }

        string probePath = Path.Combine(
            directory,
            ".imetool-storage-test-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            if (File.Exists(directory))
            {
                error = "储存位置不能是文件";
                return false;
            }

            Directory.CreateDirectory(directory);
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
}
