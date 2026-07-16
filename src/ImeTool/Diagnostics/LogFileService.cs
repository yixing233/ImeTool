using System.IO;
using System.Text;

namespace ImeTool.Diagnostics;

public sealed class LogFileService
{
    private readonly string _logPath;

    public LogFileService(string logPath)
    {
        _logPath = Path.GetFullPath(logPath);
    }

    public string LogPath => _logPath;

    public string ReadAll()
    {
        if (!File.Exists(_logPath))
        {
            return string.Empty;
        }

        using var stream = new FileStream(
            _logPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public void Export(string destinationPath)
    {
        string destination = Path.GetFullPath(destinationPath);
        string? destinationDirectory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        using var source = new FileStream(
            _logPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var destinationStream = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read);
        source.CopyTo(destinationStream);
    }
}
