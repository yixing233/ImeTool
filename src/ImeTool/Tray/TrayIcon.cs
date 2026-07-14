using System.Drawing;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace ImeTool.Tray;

public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private TrayMenuWindow? _menuWindow;
    private bool _enabled;
    private bool _startupEnabled;
    private bool _disposed;

    public TrayIcon(Settings.AppSettings settings)
    {
        _enabled = settings.Enabled;
        _startupEnabled = settings.StartWithWindows;
        _icon = LoadApplicationIcon();

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "ImeTool - 输入法状态标记",
            Icon = _icon,
            Visible = true
        };
        _notifyIcon.MouseUp += OnNotifyIconMouseUp;
        _notifyIcon.DoubleClick += (_, _) =>
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => SettingsRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? ToggleEnabledRequested;
    public event EventHandler? ToggleStartupRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public void SetEnabledChecked(bool enabled)
    {
        _enabled = enabled;
        _menuWindow?.SetState(_enabled, _startupEnabled);
    }

    public void SetStartupChecked(bool enabled)
    {
        _startupEnabled = enabled;
        _menuWindow?.SetState(_enabled, _startupEnabled);
    }

    private void OnNotifyIconMouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button != Forms.MouseButtons.Right)
        {
            return;
        }

        System.Windows.Application.Current.Dispatcher.BeginInvoke(() => ShowMenu());
    }

    public void ShowMenu()
    {
        ShowMenu(keepOpen: false);
    }

    public void ShowMenuForDevelopment()
    {
        ShowMenu(keepOpen: true);
    }

    private void ShowMenu(bool keepOpen)
    {
        Diagnostics.DiagnosticsLog.Write($"Showing tray menu, keepOpen={keepOpen}.");
        EnsureMenuWindow();
        _menuWindow!.SetState(_enabled, _startupEnabled);
        _menuWindow.ShowNearCursor(keepOpen);
    }

    private void EnsureMenuWindow()
    {
        if (_menuWindow is not null)
        {
            return;
        }

        _menuWindow = new TrayMenuWindow();
        _menuWindow.ToggleEnabledRequested += (_, _) => ToggleEnabledRequested?.Invoke(this, EventArgs.Empty);
        _menuWindow.ToggleStartupRequested += (_, _) => ToggleStartupRequested?.Invoke(this, EventArgs.Empty);
        _menuWindow.SettingsRequested += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _menuWindow.ExitRequested += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private static Icon LoadApplicationIcon()
    {
        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            Icon? extractedIcon = Icon.ExtractAssociatedIcon(processPath);
            if (extractedIcon is not null)
            {
                return extractedIcon;
            }
        }

        string localIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(localIconPath))
        {
            return new Icon(localIconPath);
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menuWindow?.Close();
        _icon.Dispose();
    }
}
