using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ImeTool.Native;

namespace ImeTool.Caret;

public sealed class JavaAccessBridgeCaretReader : IDisposable
{
    private const string BridgeDllName = "WindowsAccessBridge-64.dll";
    private IntPtr _module;
    private WindowsRunDelegate? _windowsRun;
    private IsJavaWindowDelegate? _isJavaWindow;
    private GetAccessibleContextWithFocusDelegate? _getContextWithFocus;
    private GetCaretLocationDelegate? _getCaretLocation;
    private ReleaseJavaObjectDelegate? _releaseJavaObject;
    private long _initializedAt;
    private long _lastLoadAttemptAt;
    private string? _loadFailure;

    public UiAutomationCaretReadResult Read(IntPtr expectedForeground)
    {
        if (expectedForeground == IntPtr.Zero ||
            NativeMethods.GetForegroundWindow() != expectedForeground)
        {
            return UiAutomationCaretReadResult.Failure("JAB target window is no longer foreground.");
        }

        if (!EnsureLoaded(expectedForeground))
        {
            return UiAutomationCaretReadResult.Failure(
                _loadFailure ?? "Java Access Bridge runtime was not found.");
        }

        if (Environment.TickCount64 - _initializedAt < 250)
        {
            return UiAutomationCaretReadResult.Failure("Java Access Bridge is initializing.");
        }

        try
        {
            if (_isJavaWindow?.Invoke(expectedForeground) != true)
            {
                return UiAutomationCaretReadResult.Failure("The foreground window is not exposed by JAB.");
            }

            if (_getContextWithFocus?.Invoke(expectedForeground, out int vmId, out long context) != true ||
                context == 0)
            {
                return UiAutomationCaretReadResult.Failure("JAB returned no focused accessible context.");
            }

            try
            {
                if (_getCaretLocation?.Invoke(vmId, context, out AccessibleTextRectInfo info, 0) != true ||
                    !JavaAccessBridgeGeometry.TryCreateRect(
                        info.X,
                        info.Y,
                        info.Width,
                        info.Height,
                        NativeMethods.GetDpiForWindow(expectedForeground),
                        out NativeMethods.RECT rect))
                {
                    return UiAutomationCaretReadResult.Failure("JAB returned no valid caret location.");
                }

                return UiAutomationCaretReadResult.Success(new CaretSnapshot(
                    expectedForeground,
                    expectedForeground,
                    rect,
                    CaretSource.JavaAccessBridge));
            }
            finally
            {
                _releaseJavaObject?.Invoke(vmId, context);
            }
        }
        catch (Exception exception) when (exception is SEHException or ExternalException)
        {
            return UiAutomationCaretReadResult.Failure(
                $"JAB call failed with {exception.GetType().Name}.");
        }
    }

    public void Dispose()
    {
        if (_module != IntPtr.Zero)
        {
            NativeLibrary.Free(_module);
            _module = IntPtr.Zero;
        }
    }

    private bool EnsureLoaded(IntPtr targetHwnd)
    {
        if (_module != IntPtr.Zero)
        {
            return true;
        }

        long now = Environment.TickCount64;
        if (now - _lastLoadAttemptAt < 5000)
        {
            return false;
        }

        _lastLoadAttemptAt = now;
        foreach (string candidate in EnumerateCandidates(targetHwnd))
        {
            if (!NativeLibrary.TryLoad(candidate, out IntPtr module))
            {
                continue;
            }

            try
            {
                _windowsRun = GetExport<WindowsRunDelegate>(module, "Windows_run");
                _isJavaWindow = GetExport<IsJavaWindowDelegate>(module, "isJavaWindow", "IsJavaWindow");
                _getContextWithFocus = GetExport<GetAccessibleContextWithFocusDelegate>(
                    module,
                    "getAccessibleContextWithFocus",
                    "GetAccessibleContextWithFocus");
                _getCaretLocation = GetExport<GetCaretLocationDelegate>(module, "getCaretLocation");
                _releaseJavaObject = GetExport<ReleaseJavaObjectDelegate>(module, "releaseJavaObject", "ReleaseJavaObject");
                _module = module;
                _windowsRun();
                _initializedAt = Environment.TickCount64;
                _loadFailure = null;
                return true;
            }
            catch
            {
                NativeLibrary.Free(module);
                _windowsRun = null;
                _isJavaWindow = null;
                _getContextWithFocus = null;
                _getCaretLocation = null;
                _releaseJavaObject = null;
            }
        }

        _loadFailure = $"{BridgeDllName} was not found in the application, Java, or JetBrains runtime directories.";
        return false;
    }

    private static IEnumerable<string> EnumerateCandidates(IntPtr targetHwnd)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            BridgeDllName,
            Path.Combine(AppContext.BaseDirectory, BridgeDllName)
        };

        AddJavaHomeCandidate(candidates, Environment.GetEnvironmentVariable("JAVA_HOME"));
        NativeMethods.GetWindowThreadProcessId(targetHwnd, out uint processId);
        if (processId != 0)
        {
            try
            {
                using Process process = Process.GetProcessById(checked((int)processId));
                string? executable = process.MainModule?.FileName;
                string? bin = Path.GetDirectoryName(executable);
                if (!string.IsNullOrWhiteSpace(bin))
                {
                    candidates.Add(Path.Combine(bin, BridgeDllName));
                    string installRoot = Path.GetFullPath(Path.Combine(bin, ".."));
                    candidates.Add(Path.Combine(installRoot, "jbr", "bin", BridgeDllName));
                    candidates.Add(Path.Combine(installRoot, "runtime", "bin", BridgeDllName));
                    candidates.Add(Path.GetFullPath(Path.Combine(bin, "..", "..", "jbr", "bin", BridgeDllName)));
                }
            }
            catch
            {
            }
        }

        foreach (string root in GetRuntimeSearchRoots())
        {
            try
            {
                foreach (string directory in Directory.EnumerateDirectories(root))
                {
                    candidates.Add(Path.Combine(directory, "bin", BridgeDllName));
                    candidates.Add(Path.Combine(directory, "jre", "bin", BridgeDllName));
                    candidates.Add(Path.Combine(directory, "jbr", "bin", BridgeDllName));
                    candidates.Add(Path.Combine(directory, "runtime", "bin", BridgeDllName));
                }
            }
            catch
            {
            }
        }

        return candidates;
    }

    private static IEnumerable<string> GetRuntimeSearchRoots()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs");
        return new[]
        {
            Path.Combine(programFiles, "Java"),
            Path.Combine(programFiles, "Eclipse Adoptium"),
            Path.Combine(programFiles, "JetBrains"),
            Path.Combine(localPrograms, "JetBrains")
        }.Where(Directory.Exists);
    }

    private static void AddJavaHomeCandidate(HashSet<string> candidates, string? javaHome)
    {
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            candidates.Add(Path.Combine(javaHome, "bin", BridgeDllName));
        }
    }

    private static T GetExport<T>(IntPtr module, params string[] names) where T : Delegate
    {
        foreach (string name in names)
        {
            if (NativeLibrary.TryGetExport(module, name, out IntPtr address))
            {
                return Marshal.GetDelegateForFunctionPointer<T>(address);
            }
        }

        throw new EntryPointNotFoundException(string.Join(" / ", names));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccessibleTextRectInfo
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void WindowsRunDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool IsJavaWindowDelegate(IntPtr hwnd);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool GetAccessibleContextWithFocusDelegate(
        IntPtr hwnd,
        out int vmId,
        out long accessibleContext);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool GetCaretLocationDelegate(
        int vmId,
        long accessibleContext,
        out AccessibleTextRectInfo rect,
        int index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ReleaseJavaObjectDelegate(int vmId, long accessibleContext);
}

public static class JavaAccessBridgeGeometry
{
    public static bool TryCreateRect(
        int x,
        int y,
        int width,
        int height,
        uint dpi,
        out NativeMethods.RECT rect)
    {
        rect = default;
        if (height <= 0 || height > 500 || width > 500 ||
            (x == 0 && y == 0 && width == 0 && height == 0))
        {
            return false;
        }

        double scale = (dpi == 0 ? 96u : dpi) / 96d;
        int scaledX = (int)Math.Round(x * scale);
        int scaledY = (int)Math.Round(y * scale);
        int scaledWidth = Math.Max(1, (int)Math.Round(width * scale));
        int scaledHeight = Math.Max(1, (int)Math.Round(height * scale));
        rect = new NativeMethods.RECT
        {
            Left = scaledX,
            Top = scaledY,
            Right = scaledX + scaledWidth,
            Bottom = scaledY + scaledHeight
        };
        return true;
    }
}
