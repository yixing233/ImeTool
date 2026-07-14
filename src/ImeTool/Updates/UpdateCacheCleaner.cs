using System.IO;

namespace ImeTool.Updates;

public static class UpdateCacheCleaner
{
    public static void Cleanup(string? updatesRoot = null)
    {
        string root = updatesRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImeTool",
            "Updates");
        if (!Directory.Exists(root))
        {
            return;
        }

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // The installer currently performing this update may still be running.
        }
        catch (UnauthorizedAccessException)
        {
            // Cache cleanup must never prevent the tray application from starting.
        }
    }
}
