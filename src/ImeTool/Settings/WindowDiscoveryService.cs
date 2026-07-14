using System.Diagnostics;
using System.Text;
using ImeTool.Native;

namespace ImeTool.Settings;

public sealed record DetectedWindow(
    IntPtr Handle,
    uint ProcessId,
    string ProcessName,
    string Title,
    bool IsForeground)
{
    public string DisplayName => $"{ProcessName}.exe  ·  {Title}";
}

public sealed class WindowDiscoveryService
{
    public IReadOnlyList<DetectedWindow> GetVisibleWindows()
    {
        var candidates = new List<DetectedWindow>();
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        uint currentProcessId = checked((uint)Environment.ProcessId);

        NativeMethods.EnumWindows((handle, _) =>
        {
            try
            {
                if (!NativeMethods.IsWindowVisible(handle))
                {
                    return true;
                }

                string title = ReadWindowTitle(handle);
                if (string.IsNullOrWhiteSpace(title))
                {
                    return true;
                }

                NativeMethods.GetWindowThreadProcessId(handle, out uint processId);
                if (processId == 0)
                {
                    return true;
                }

                using Process process = Process.GetProcessById(checked((int)processId));
                candidates.Add(new DetectedWindow(
                    handle,
                    processId,
                    process.ProcessName,
                    title,
                    handle == foreground));
            }
            catch
            {
                // Windows can disappear or become inaccessible while they are enumerated.
            }

            return true;
        }, IntPtr.Zero);

        return NormalizeCandidates(candidates, currentProcessId);
    }

    public static IReadOnlyList<DetectedWindow> NormalizeCandidates(
        IEnumerable<DetectedWindow> candidates,
        uint currentProcessId)
    {
        return candidates
            .Where(window => window.Handle != IntPtr.Zero && window.ProcessId != currentProcessId)
            .Select(window => window with
            {
                ProcessName = ApplicationRuleNormalizer.NormalizeProcessName(window.ProcessName),
                Title = window.Title.Trim()
            })
            .Where(window => !string.IsNullOrWhiteSpace(window.ProcessName) &&
                             !string.IsNullOrWhiteSpace(window.Title))
            .GroupBy(window => window.Handle)
            .Select(group => group.First())
            .OrderByDescending(window => window.IsForeground)
            .ThenBy(window => window.ProcessName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static string ReadWindowTitle(IntPtr handle)
    {
        int length = NativeMethods.GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var title = new StringBuilder(length + 1);
        return NativeMethods.GetWindowText(handle, title, title.Capacity) > 0
            ? title.ToString()
            : string.Empty;
    }
}

public static class ApplicationRuleTextEditor
{
    public static string AddProcessName(string? currentText, string? processName)
    {
        string normalizedProcessName = ApplicationRuleNormalizer.NormalizeProcessName(processName);
        var processNames = (currentText ?? string.Empty)
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ApplicationRuleNormalizer.NormalizeProcessName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(normalizedProcessName) &&
            !processNames.Contains(normalizedProcessName, StringComparer.OrdinalIgnoreCase))
        {
            processNames.Add(normalizedProcessName);
        }

        return string.Join(Environment.NewLine, processNames);
    }
}
