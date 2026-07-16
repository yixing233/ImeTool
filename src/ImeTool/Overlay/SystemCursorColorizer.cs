using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ImeTool.Diagnostics;
using ImeTool.Native;
using ImeTool.Settings;

namespace ImeTool.Overlay;

public static class IBeamCursorRenderer
{
    public const int DefaultSize = 32;

    public static Bitmap Render(Color color, int size = DefaultSize)
    {
        size = Math.Clamp(size, 24, 64);
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float center = size / 2f;
        float top = size * 0.16f;
        float bottom = size * 0.84f;
        float arm = size * 0.18f;
        using var outline = new Pen(Color.FromArgb(225, 20, 20, 20), Math.Max(4f, size / 7f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        using var lightEdge = new Pen(Color.FromArgb(235, 255, 255, 255), Math.Max(2.6f, size / 10f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        using var statePen = new Pen(color, Math.Max(1.6f, size / 16f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        DrawIBeam(graphics, outline, center, top, bottom, arm);
        DrawIBeam(graphics, lightEdge, center, top, bottom, arm);
        DrawIBeam(graphics, statePen, center, top, bottom, arm);
        return bitmap;
    }

    private static void DrawIBeam(
        Graphics graphics,
        Pen pen,
        float center,
        float top,
        float bottom,
        float arm)
    {
        graphics.DrawLine(pen, center, top, center, bottom);
        graphics.DrawLine(pen, center - arm, top, center + arm, top);
        graphics.DrawLine(pen, center - arm, bottom, center + arm, bottom);
    }
}

public sealed class SystemCursorColorizer : IDisposable
{
    private MarkerState _lastState = MarkerState.Unknown;
    private string? _lastColor;
    private bool _isApplied;
    private bool _disposed;

    public void Update(MarkerState state, MarkerAppearanceSettings settings)
    {
        if (_disposed)
        {
            return;
        }

        if (state == MarkerState.Unknown)
        {
            Restore();
            return;
        }

        string colorText = IndicatorColorResolver.GetColor(state, settings);
        if (_isApplied && _lastState == state && string.Equals(_lastColor, colorText, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Color color;
        try
        {
            color = ColorTranslator.FromHtml(colorText);
        }
        catch
        {
            color = Color.Gray;
        }

        IntPtr cursor = CreateCursor(color);
        if (cursor == IntPtr.Zero)
        {
            return;
        }

        if (!NativeMethods.SetSystemCursor(cursor, NativeMethods.OcrIBeam))
        {
            NativeMethods.DestroyIcon(cursor);
            string message = $"SetSystemCursor failed: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}.";
            Debug.WriteLine(message);
            DiagnosticsLog.Warn(message);
            return;
        }

        _lastState = state;
        _lastColor = colorText;
        _isApplied = true;
    }

    public void Restore()
    {
        if (!_isApplied)
        {
            _lastState = MarkerState.Unknown;
            _lastColor = null;
            return;
        }

        NativeMethods.SystemParametersInfo(
            NativeMethods.SpiSetCursors,
            0,
            IntPtr.Zero,
            0);
        _isApplied = false;
        _lastState = MarkerState.Unknown;
        _lastColor = null;
    }

    private static IntPtr CreateCursor(Color color)
    {
        using Bitmap bitmap = IBeamCursorRenderer.Render(color);
        IntPtr temporaryIcon = bitmap.GetHicon();
        if (temporaryIcon == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        try
        {
            if (!NativeMethods.GetIconInfo(temporaryIcon, out NativeMethods.ICONINFO info))
            {
                return IntPtr.Zero;
            }

            try
            {
                info.fIcon = false;
                info.xHotspot = IBeamCursorRenderer.DefaultSize / 2;
                info.yHotspot = IBeamCursorRenderer.DefaultSize / 2;
                return NativeMethods.CreateIconIndirect(ref info);
            }
            finally
            {
                if (info.hbmMask != IntPtr.Zero)
                {
                    NativeMethods.DeleteObject(info.hbmMask);
                }

                if (info.hbmColor != IntPtr.Zero)
                {
                    NativeMethods.DeleteObject(info.hbmColor);
                }
            }
        }
        finally
        {
            NativeMethods.DestroyIcon(temporaryIcon);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Restore();
        _disposed = true;
    }
}
