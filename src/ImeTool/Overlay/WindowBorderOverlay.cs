using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ImeTool.Native;
using MediaColor = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace ImeTool.Overlay;

public static class WindowBorderGeometry
{
    public static bool TryNormalize(NativeMethods.RECT rect, out NativeMethods.RECT bounds)
    {
        bounds = rect;
        return rect.Right > rect.Left && rect.Bottom > rect.Top;
    }
}

public sealed class WindowBorderOverlay : Window
{
    private readonly Border _border;
    private IntPtr _hwnd;

    public WindowBorderOverlay()
    {
        Width = 1;
        Height = 1;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = MediaBrushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Visibility = Visibility.Hidden;
        _border = new Border
        {
            Background = MediaBrushes.Transparent,
            SnapsToDevicePixels = true
        };
        Content = _border;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyExtendedStyles();
        };
    }

    public void Update(IntPtr targetWindow, MarkerState state, string color, int widthPixels)
    {
        if (targetWindow == IntPtr.Zero ||
            state == MarkerState.Unknown ||
            !NativeMethods.IsWindow(targetWindow) ||
            !NativeMethods.IsWindowVisible(targetWindow) ||
            NativeMethods.IsIconic(targetWindow) ||
            !NativeMethods.GetWindowRect(targetWindow, out NativeMethods.RECT rawBounds) ||
            !WindowBorderGeometry.TryNormalize(rawBounds, out NativeMethods.RECT bounds))
        {
            HideIndicator();
            return;
        }

        if (_hwnd == IntPtr.Zero)
        {
            Show();
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyExtendedStyles();
        }
        else if (!IsVisible)
        {
            Show();
        }

        double dpi = Math.Max(96, NativeMethods.GetDpiForWindow(targetWindow));
        double thicknessDip = Math.Clamp(widthPixels, 1, 12) * 96d / dpi;
        _border.BorderThickness = new Thickness(thicknessDip);
        _border.BorderBrush = new SolidColorBrush(ParseColor(color));

        NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HwndTopmost,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    public void HideIndicator()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    private static MediaColor ParseColor(string color)
    {
        try
        {
            return (MediaColor)(MediaColorConverter.ConvertFromString(color) ?? Colors.Gray);
        }
        catch
        {
            return Colors.Gray;
        }
    }

    private void ApplyExtendedStyles()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        IntPtr current = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GwlExStyle);
        long style = current.ToInt64() |
                     NativeMethods.WsExTransparent |
                     NativeMethods.WsExToolWindow |
                     NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GwlExStyle, new IntPtr(style));
    }
}
