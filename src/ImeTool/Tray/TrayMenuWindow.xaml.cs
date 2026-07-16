using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using ImeTool.Diagnostics;
using ImeTool.Native;
using Forms = System.Windows.Forms;

namespace ImeTool.Tray;

public partial class TrayMenuWindow : Window
{
    private bool _updatingState;
    private bool _keepOpen;
    private bool _isHiding;
    private bool _dismissQueued;
    private IntPtr _windowHandle;
    private IntPtr _mouseHook;
    private NativeMethods.LowLevelMouseProc? _mouseHookProc;
    private DateTimeOffset _shownAt;

    public TrayMenuWindow()
    {
        InitializeComponent();
    }

    public event EventHandler? ToggleEnabledRequested;
    public event EventHandler? ToggleStartupRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public void SetState(bool enabled, bool startupEnabled)
    {
        _updatingState = true;
        EnabledToggle.IsChecked = enabled;
        StartupToggle.IsChecked = startupEnabled;
        StatusText.Text = enabled ? "标记已启用" : "标记已暂停";
        _updatingState = false;
    }

    public void ShowNearCursor(bool keepOpen = false)
    {
        _keepOpen = keepOpen;
        _dismissQueued = false;
        _shownAt = DateTimeOffset.Now;
        StopOutsideClickHook();
        Opacity = 0;
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();

        System.Drawing.Point cursor = Forms.Cursor.Position;
        System.Drawing.Rectangle workingArea = Forms.Screen.FromPoint(cursor).WorkingArea;
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double scaleX = dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
        double scaleY = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;
        double widthPixels = ActualWidth * scaleX;
        double heightPixels = ActualHeight * scaleY;

        double leftPixels = Math.Min(cursor.X + 6, workingArea.Right - widthPixels - 8);
        leftPixels = Math.Max(workingArea.Left + 8, leftPixels);

        double topPixels = cursor.Y + 6;
        if (topPixels + heightPixels > workingArea.Bottom - 8)
        {
            topPixels = cursor.Y - heightPixels - 6;
        }
        topPixels = Math.Max(workingArea.Top + 8, topPixels);

        Left = leftPixels / scaleX;
        Top = topPixels / scaleY;
        Opacity = 1;

        if (!_keepOpen)
        {
            _windowHandle = new WindowInteropHelper(this).Handle;
            StartOutsideClickHook();
        }
    }

    private void OnEnabledToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingState)
        {
            return;
        }

        bool enabled = EnabledToggle.IsChecked == true;
        StatusText.Text = enabled ? "标记已启用" : "标记已暂停";
        ToggleEnabledRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnStartupToggleChanged(object sender, RoutedEventArgs e)
    {
        if (!_updatingState)
        {
            ToggleStartupRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        HideMenu("settings-command");
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        HideMenu("exit-command");
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideMenu("escape-key");
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        StopOutsideClickHook();
        base.OnClosed(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _windowHandle = new WindowInteropHelper(this).Handle;
        IntPtr extendedStyle = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle);
        long noActivateStyle = extendedStyle.ToInt64() |
                               NativeMethods.WsExNoActivate |
                               NativeMethods.WsExToolWindow;
        NativeMethods.SetWindowLongPtr(
            _windowHandle,
            NativeMethods.GwlExStyle,
            new IntPtr(noActivateStyle));

        if (HwndSource.FromHwnd(_windowHandle) is HwndSource source)
        {
            source.AddHook(WindowMessageHook);
        }
    }

    private static IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == NativeMethods.WmMouseActivate)
        {
            handled = true;
            return new IntPtr(NativeMethods.MaNoActivate);
        }

        return IntPtr.Zero;
    }

    private void HideMenu(string reason)
    {
        if (_isHiding || !IsVisible)
        {
            return;
        }

        _isHiding = true;
        DiagnosticsLog.Write($"Tray menu hidden: reason={reason}.");
        StopOutsideClickHook();
        Hide();
        _isHiding = false;
    }

    private void StartOutsideClickHook()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            return;
        }

        _mouseHookProc ??= OnLowLevelMouse;
        _mouseHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLl,
            _mouseHookProc,
            NativeMethods.GetModuleHandle(null),
            0);

        if (_mouseHook == IntPtr.Zero)
        {
            DiagnosticsLog.Warn($"Tray menu outside-click hook failed: error={Marshal.GetLastWin32Error()}.");
        }
    }

    private void StopOutsideClickHook()
    {
        if (_mouseHook == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_mouseHook);
        _mouseHook = IntPtr.Zero;
    }

    private IntPtr OnLowLevelMouse(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 &&
            IsVisible &&
            !_keepOpen &&
            !_isHiding &&
            !_dismissQueued &&
            TrayMenuDismissPolicy.IsButtonDownMessage(wParam.ToInt32()))
        {
            NativeMethods.MSLLHOOKSTRUCT mouse = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            NativeMethods.GetCursorPos(out NativeMethods.POINT cursor);
            IntPtr hitWindow = NativeMethods.WindowFromPoint(cursor);
            IntPtr hitRoot = hitWindow == IntPtr.Zero
                ? IntPtr.Zero
                : NativeMethods.GetAncestor(hitWindow, NativeMethods.GaRoot);
            bool inside = hitRoot == _windowHandle;

            if (!inside)
            {
                double elapsedMilliseconds = (DateTimeOffset.Now - _shownAt).TotalMilliseconds;
                DiagnosticsLog.Write(
                    $"Tray menu outside mouse down detected: message=0x{wParam.ToInt32():X4}, " +
                    $"hookPoint=({mouse.pt.X},{mouse.pt.Y}), cursor=({cursor.X},{cursor.Y}), " +
                    $"hit=0x{hitWindow.ToInt64():X}, hitRoot=0x{hitRoot.ToInt64():X}, menu=0x{_windowHandle.ToInt64():X}, " +
                    $"flags=0x{mouse.flags:X}, elapsedMs={elapsedMilliseconds:F0}.");
                _dismissQueued = true;
                Dispatcher.BeginInvoke(() =>
                {
                    _dismissQueued = false;
                    HideMenu($"outside-mouse-down-0x{wParam.ToInt32():X4}");
                }, DispatcherPriority.Send);
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }
}
