using ImeTool.Settings;

namespace ImeTool.Tests.Settings;

public sealed class WindowDiscoveryServiceTests
{
    [Fact]
    public void NormalizeCandidates_FiltersInvalidAndOwnWindows()
    {
        DetectedWindow[] candidates =
        [
            new(new IntPtr(1), 10, "ImeTool", "设置", false),
            new(new IntPtr(2), 20, "chrome.exe", "Example", false),
            new(new IntPtr(3), 30, "", "无进程", false),
            new(new IntPtr(4), 40, "Code", "   ", false)
        ];

        IReadOnlyList<DetectedWindow> result = WindowDiscoveryService.NormalizeCandidates(candidates, 10);

        DetectedWindow window = Assert.Single(result);
        Assert.Equal("chrome", window.ProcessName);
        Assert.Equal("Example", window.Title);
    }

    [Fact]
    public void NormalizeCandidates_PutsForegroundFirstAndRemovesDuplicates()
    {
        DetectedWindow[] candidates =
        [
            new(new IntPtr(1), 20, "Code", "main.cs", false),
            new(new IntPtr(1), 20, "Code", "main.cs", false),
            new(new IntPtr(2), 30, "notepad", "notes.txt", true)
        ];

        IReadOnlyList<DetectedWindow> result = WindowDiscoveryService.NormalizeCandidates(candidates, 10);

        Assert.Equal(2, result.Count);
        Assert.True(result[0].IsForeground);
        Assert.Equal("notepad", result[0].ProcessName);
    }

    [Fact]
    public void RuleTextEditor_AppendsNormalizedProcessOnlyOnce()
    {
        string once = ApplicationRuleTextEditor.AddProcessName("Code\r\nchrome", "NOTEPAD.EXE");
        string twice = ApplicationRuleTextEditor.AddProcessName(once, "notepad");

        Assert.Equal("Code\r\nchrome\r\nNOTEPAD", once);
        Assert.Equal(once, twice);
    }
}
