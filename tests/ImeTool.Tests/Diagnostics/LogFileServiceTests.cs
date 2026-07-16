using System.Text;
using ImeTool.Diagnostics;

namespace ImeTool.Tests.Diagnostics;

public sealed class LogFileServiceTests
{
    [Fact]
    public void ReadAll_ReturnsEmptyWhenLogDoesNotExist()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "ImeTool.log");

        Assert.Equal(string.Empty, new LogFileService(path).ReadAll());
    }

    [Fact]
    public void ReadAll_CanReadFileSharedByWriter()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "ImeTool.log");
        using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        byte[] bytes = Encoding.UTF8.GetBytes("line one\r\nline two\r\n");
        writer.Write(bytes);
        writer.Flush();

        string content = new LogFileService(path).ReadAll();

        Assert.Contains("line one", content);
        Assert.Contains("line two", content);
    }

    [Fact]
    public void Export_CopiesCurrentLogContent()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "ImeTool.log");
        string destination = Path.Combine(directory, "exports", "diagnostics.log");
        File.WriteAllText(source, "[ERROR] sample");

        new LogFileService(source).Export(destination);

        Assert.Equal("[ERROR] sample", File.ReadAllText(destination));
    }
}
