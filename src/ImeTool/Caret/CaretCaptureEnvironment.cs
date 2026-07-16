using System.Diagnostics;
using System.IO;
using System.Text;
using ImeTool.Native;

namespace ImeTool.Caret;

public enum CaretTargetEnvironment
{
    Standard = 0,
    ChromiumBrowser = 1,
    FirefoxBrowser = 2,
    Java = 3
}

public static class CaretCaptureEnvironmentClassifier
{
    private static readonly HashSet<string> ChromiumProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "brave", "vivaldi", "opera", "opera_gx", "arc", "chromium"
    };

    private static readonly HashSet<string> JavaProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "idea64", "pycharm64", "webstorm64", "clion64", "goland64", "datagrip64",
        "rubymine64", "phpstorm64", "rider64", "studio64", "java", "javaw"
    };

    public static CaretTargetEnvironment Classify(string? processName, string? windowClass)
    {
        string process = Path.GetFileNameWithoutExtension(processName?.Trim() ?? string.Empty);
        string className = windowClass?.Trim() ?? string.Empty;
        if (string.Equals(process, "firefox", StringComparison.OrdinalIgnoreCase) ||
            className.Contains("MozillaWindowClass", StringComparison.OrdinalIgnoreCase))
        {
            return CaretTargetEnvironment.FirefoxBrowser;
        }

        if (ChromiumProcesses.Contains(process) ||
            className.StartsWith("Chrome_WidgetWin_", StringComparison.OrdinalIgnoreCase))
        {
            return CaretTargetEnvironment.ChromiumBrowser;
        }

        if (JavaProcesses.Contains(process) ||
            className.StartsWith("SunAwt", StringComparison.OrdinalIgnoreCase))
        {
            return CaretTargetEnvironment.Java;
        }

        return CaretTargetEnvironment.Standard;
    }

    public static CaretTargetEnvironment Detect(IntPtr foregroundHwnd)
    {
        string processName = string.Empty;
        NativeMethods.GetWindowThreadProcessId(foregroundHwnd, out uint processId);
        if (processId != 0)
        {
            try
            {
                using Process process = Process.GetProcessById(checked((int)processId));
                processName = process.ProcessName;
            }
            catch
            {
            }
        }

        var className = new StringBuilder(256);
        string windowClass = NativeMethods.GetClassName(
            foregroundHwnd,
            className,
            className.Capacity) > 0
            ? className.ToString()
            : string.Empty;
        return Classify(processName, windowClass);
    }
}

public static class BrowserCaretCompatibilityPolicy
{
    public static bool TrySelect(
        CaretTargetEnvironment environment,
        CaretSnapshot? automationSnapshot,
        CaretSnapshot? msaaSnapshot,
        CaretSnapshot? trustedNativeSnapshot,
        out CaretSnapshot snapshot)
    {
        CaretSnapshot? selected = environment == CaretTargetEnvironment.FirefoxBrowser
            ? msaaSnapshot ?? automationSnapshot ?? trustedNativeSnapshot
            : automationSnapshot ?? msaaSnapshot ?? trustedNativeSnapshot;
        if (selected is not CaretSnapshot value)
        {
            snapshot = default;
            return false;
        }

        CaretSource source = value.Source switch
        {
            CaretSource.Msaa => CaretSource.BrowserMsaa,
            CaretSource.GuiThreadInfo => CaretSource.BrowserWin32,
            _ => CaretSource.BrowserUiAutomation
        };
        snapshot = value with { Source = source };
        return true;
    }
}
