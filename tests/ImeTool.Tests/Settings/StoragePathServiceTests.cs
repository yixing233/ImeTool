using ImeTool.Settings;

namespace ImeTool.Tests.Settings;

public sealed class StoragePathServiceTests
{
    [Fact]
    public void ResolveDirectory_UsesApplicationDirectoryForBlankValue()
    {
        Assert.Equal(
            Path.GetFullPath(AppContext.BaseDirectory),
            StoragePathService.ResolveDirectory(" "),
            ignoreCase: true);
    }

    [Fact]
    public void DataFilesShareConfiguredStorageDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.Equal(
            Path.Combine(directory, StoragePathService.LogFileName),
            StoragePathService.GetLogPath(directory),
            ignoreCase: true);
        Assert.Equal(
            Path.Combine(directory, StoragePathService.WindowMemoryFileName),
            StoragePathService.GetWindowMemoryPath(directory),
            ignoreCase: true);
    }

    [Fact]
    public void TryProbeWrite_CreatesWritableDirectoryAndRemovesProbeFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.True(StoragePathService.TryProbeWrite(directory, out string? error), error);
        Assert.True(Directory.Exists(directory));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public void TryProbeWrite_RejectsFileAsStorageDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "data.bin");
        File.WriteAllText(file, "data");

        Assert.False(StoragePathService.TryProbeWrite(file, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
