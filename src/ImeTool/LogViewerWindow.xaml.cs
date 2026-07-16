using System.IO;
using System.Windows;
using ImeTool.Diagnostics;
using ImeTool.Settings;
using WpfBackdropType = Wpf.Ui.Controls.WindowBackdropType;

namespace ImeTool;

public partial class LogViewerWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly LogFileService _logFileService;
    private readonly DiagnosticsLogLevel _captureLevel;

    public LogViewerWindow(
        string logPath,
        DiagnosticsLogLevel captureLevel,
        SettingsWindowBackdrop backdrop)
    {
        InitializeComponent();
        _logFileService = new LogFileService(logPath);
        _captureLevel = DiagnosticsLogLevelPolicy.Normalize(captureLevel);
        WindowBackdropType = backdrop == SettingsWindowBackdrop.Acrylic
            ? WpfBackdropType.Acrylic
            : WpfBackdropType.Mica;
        CaptureLevelText.Text = DiagnosticsLogLevelPolicy.Label(_captureLevel);
        LogPathText.Text = _logFileService.LogPath;
        LogPathText.ToolTip = _logFileService.LogPath;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await RefreshLogAsync();
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e) =>
        await RefreshLogAsync();

    private async Task RefreshLogAsync()
    {
        ActionStatusText.Text = "正在刷新…";
        try
        {
            await DiagnosticsLog.FlushAsync();
            string content = _logFileService.ReadAll();
            LogTextBox.Text = content;
            LogTextBox.ScrollToEnd();
            bool hasContent = content.Length > 0;
            CopyButton.IsEnabled = hasContent;
            ExportButton.IsEnabled = File.Exists(_logFileService.LogPath);
            LogSummaryText.Text = hasContent
                ? $"{CountLines(content)} 行 · {FormatFileSize(new FileInfo(_logFileService.LogPath).Length)}"
                : "当前还没有符合捕获等级的日志";
            ActionStatusText.Text = "已刷新";
        }
        catch (Exception exception)
        {
            CopyButton.IsEnabled = false;
            ExportButton.IsEnabled = false;
            LogSummaryText.Text = "日志读取失败";
            ActionStatusText.Text = exception.Message;
        }
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        string text = string.IsNullOrEmpty(LogTextBox.SelectedText)
            ? LogTextBox.Text
            : LogTextBox.SelectedText;
        if (string.IsNullOrEmpty(text))
        {
            ActionStatusText.Text = "暂无可复制内容";
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(text);
            ActionStatusText.Text = string.IsNullOrEmpty(LogTextBox.SelectedText)
                ? "已复制全部日志"
                : "已复制选中日志";
        }
        catch (Exception exception)
        {
            ActionStatusText.Text = $"复制失败：{exception.Message}";
        }
    }

    private void OnExportClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出 ImeTool 运行日志",
            Filter = "日志文件|*.log|文本文件|*.txt|所有文件|*.*",
            AddExtension = true,
            DefaultExt = ".log",
            FileName = $"ImeTool-log-{DateTime.Now:yyyyMMdd-HHmmss}.log"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _logFileService.Export(dialog.FileName);
            ActionStatusText.Text = $"已导出到 {dialog.FileName}";
        }
        catch (Exception exception)
        {
            ActionStatusText.Text = $"导出失败：{exception.Message}";
        }
    }

    private static int CountLines(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        int count = 1;
        foreach (char character in text)
        {
            if (character == '\n')
            {
                count++;
            }
        }

        return text.EndsWith('\n') ? count - 1 : count;
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 => $"{bytes / 1024d / 1024d:F2} MB",
        >= 1024 => $"{bytes / 1024d:F1} KB",
        _ => $"{bytes} B"
    };

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
