using ImeTool.Diagnostics;

namespace ImeTool.Tests.Diagnostics;

[CollectionDefinition("DiagnosticsLog", DisableParallelization = true)]
public sealed class DiagnosticsLogCollection
{
}

[Collection("DiagnosticsLog")]
public sealed class DiagnosticsLogLevelTests
{
    [Theory]
    [InlineData(DiagnosticsLogLevel.Info, DiagnosticsLogLevel.Info, true)]
    [InlineData(DiagnosticsLogLevel.Info, DiagnosticsLogLevel.Warn, true)]
    [InlineData(DiagnosticsLogLevel.Warn, DiagnosticsLogLevel.Info, false)]
    [InlineData(DiagnosticsLogLevel.Warn, DiagnosticsLogLevel.Warn, true)]
    [InlineData(DiagnosticsLogLevel.Warn, DiagnosticsLogLevel.Error, true)]
    [InlineData(DiagnosticsLogLevel.Error, DiagnosticsLogLevel.Warn, false)]
    [InlineData(DiagnosticsLogLevel.Error, DiagnosticsLogLevel.Error, true)]
    public void ShouldCapture_AppliesMinimumSeverity(
        DiagnosticsLogLevel minimum,
        DiagnosticsLogLevel message,
        bool expected)
    {
        Assert.Equal(expected, DiagnosticsLogLevelPolicy.ShouldCapture(minimum, message));
    }

    [Fact]
    public void Normalize_UsesWarnForUnknownValue()
    {
        Assert.Equal(
            DiagnosticsLogLevel.Warn,
            DiagnosticsLogLevelPolicy.Normalize((DiagnosticsLogLevel)999));
    }

    [Fact]
    public void FormatLine_IncludesTimestampLevelAndMessage()
    {
        string line = DiagnosticsLog.FormatLine(
            new DateTimeOffset(2026, 7, 16, 12, 34, 56, 123, TimeSpan.FromHours(8)),
            DiagnosticsLogLevel.Warn,
            "sample warning");

        Assert.Contains("2026-07-16 12:34:56.123 +08:00", line);
        Assert.Contains("[WARN]", line);
        Assert.Contains("sample warning", line);
    }

    [Fact]
    public async Task Configure_WritesOnlyMessagesAtOrAboveSelectedLevel()
    {
        await DiagnosticsLog.FlushAsync();
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string infoMessage = "filtered-info-" + Guid.NewGuid().ToString("N");
        string warnMessage = "captured-warn-" + Guid.NewGuid().ToString("N");
        string errorMessage = "captured-error-" + Guid.NewGuid().ToString("N");
        try
        {
            DiagnosticsLog.Configure(directory, DiagnosticsLogLevel.Warn);
            DiagnosticsLog.Info(infoMessage);
            DiagnosticsLog.Warn(warnMessage);
            DiagnosticsLog.Error(errorMessage);
            await DiagnosticsLog.FlushAsync();

            string content = File.ReadAllText(DiagnosticsLog.LogPath);
            Assert.DoesNotContain(infoMessage, content);
            Assert.Contains($"[WARN] {warnMessage}", content);
            Assert.Contains($"[ERROR] {errorMessage}", content);
        }
        finally
        {
            DiagnosticsLog.Configure(
                ImeTool.Settings.StoragePathService.DefaultDirectory,
                DiagnosticsLogLevel.Warn);
        }
    }
}
