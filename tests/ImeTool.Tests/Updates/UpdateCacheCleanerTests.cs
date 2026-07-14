using ImeTool.Updates;

namespace ImeTool.Tests.Updates;

public sealed class UpdateCacheCleanerTests
{
    [Fact]
    public void Cleanup_RemovesDownloadedUpdateDirectories()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string versionDirectory = Path.Combine(root, "v1.2.3");
        Directory.CreateDirectory(versionDirectory);
        File.WriteAllText(Path.Combine(versionDirectory, "ImeTool_Windows_x64.exe"), "cached");

        UpdateCacheCleaner.Cleanup(root);

        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Cleanup_IgnoresMissingDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Exception? exception = Record.Exception(() => UpdateCacheCleaner.Cleanup(root));

        Assert.Null(exception);
    }
}
