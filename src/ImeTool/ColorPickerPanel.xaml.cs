using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ImeTool.Settings;
using MediaColor = System.Windows.Media.Color;
using WpfButton = System.Windows.Controls.Button;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace ImeTool;

public partial class ColorPickerPanel : WpfUserControl
{
    private double _hue;
    private double _saturation;
    private double _value;
    private bool _updatingFields;
    private bool _draggingSpectrum;
    private bool _draggingHue;

    public ColorPickerPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateThumbPositions();
        PreviewKeyDown += OnPanelPreviewKeyDown;
    }

    public MediaColor SelectedColor { get; private set; }

    public event EventHandler? Confirmed;

    public event EventHandler? Canceled;

    public event EventHandler? DragRequested;

    public void ApplyBackdrop(SettingsWindowBackdrop backdrop)
    {
        bool acrylic = backdrop == SettingsWindowBackdrop.Acrylic;
        // Leave the main chrome transparent so the window's real DWM material
        // is visible. Cards and footer use the same densities as SettingsWindow.
        Resources["PickerChromeBrush"] = CreateBrush(0x00, 0xFF, 0xFF, 0xFF);
        Resources["PickerSurfaceBrush"] = CreateBrush(acrylic ? 0x78 : 0xBF, 0xFF, 0xFF, 0xFF);
        Resources["PickerBorderBrush"] = CreateBrush(acrylic ? 0x70 : 0x80, 0xFF, 0xFF, 0xFF);
        Resources["PickerFooterBrush"] = CreateBrush(acrylic ? 0x72 : 0xB8, 0xFA, 0xFA, 0xFA);
    }

    private static SolidColorBrush CreateBrush(int alpha, byte red, byte green, byte blue) =>
        new(MediaColor.FromArgb((byte)alpha, red, green, blue));

    public void Open(MediaColor initialColor)
    {
        SetSelectedColor(initialColor);
        Dispatcher.BeginInvoke(() =>
        {
            Focus();
            HexBox.Focus();
            HexBox.SelectAll();
        });
    }

    private void SetSelectedColor(MediaColor color)
    {
        (_hue, _saturation, _value) = ColorMath.ToHsv(color);
        UpdateColorFromHsv();
    }

    private void UpdateColorFromHsv()
    {
        SelectedColor = ColorMath.FromHsv(_hue, _saturation, _value);
        SpectrumHue.Background = new SolidColorBrush(ColorMath.FromHsv(_hue, 1, 1));
        SelectedColorSwatch.Background = new SolidColorBrush(SelectedColor);
        SelectedHexText.Text = ColorMath.ToHex(SelectedColor);

        _updatingFields = true;
        HexBox.Text = ColorMath.ToHex(SelectedColor);
        RedBox.Text = SelectedColor.R.ToString(CultureInfo.InvariantCulture);
        GreenBox.Text = SelectedColor.G.ToString(CultureInfo.InvariantCulture);
        BlueBox.Text = SelectedColor.B.ToString(CultureInfo.InvariantCulture);
        _updatingFields = false;

        UpdateThumbPositions();
    }

    private void UpdateThumbPositions()
    {
        if (SpectrumSurface.ActualWidth > 0 && SpectrumSurface.ActualHeight > 0)
        {
            Canvas.SetLeft(SpectrumThumb, _saturation * SpectrumSurface.ActualWidth - SpectrumThumb.Width / 2);
            Canvas.SetTop(SpectrumThumb, (1 - _value) * SpectrumSurface.ActualHeight - SpectrumThumb.Height / 2);
        }

        if (HueSurface.ActualWidth > 0)
        {
            Canvas.SetLeft(HueThumb, _hue / 360 * HueSurface.ActualWidth - HueThumb.Width / 2);
        }
    }

    private void UpdateSpectrum(WpfPoint point)
    {
        if (SpectrumSurface.ActualWidth <= 0 || SpectrumSurface.ActualHeight <= 0)
        {
            return;
        }

        _saturation = Math.Clamp(point.X / SpectrumSurface.ActualWidth, 0, 1);
        _value = 1 - Math.Clamp(point.Y / SpectrumSurface.ActualHeight, 0, 1);
        UpdateColorFromHsv();
    }

    private void UpdateHue(WpfPoint point)
    {
        if (HueSurface.ActualWidth <= 0)
        {
            return;
        }

        _hue = Math.Clamp(point.X / HueSurface.ActualWidth, 0, 1) * 360;
        if (_hue >= 360)
        {
            _hue = 0;
        }

        UpdateColorFromHsv();
    }

    private void OnSpectrumMouseDown(object sender, MouseButtonEventArgs e)
    {
        _draggingSpectrum = true;
        SpectrumSurface.CaptureMouse();
        UpdateSpectrum(e.GetPosition(SpectrumSurface));
    }

    private void OnSpectrumMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_draggingSpectrum && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateSpectrum(e.GetPosition(SpectrumSurface));
        }
    }

    private void OnSpectrumMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_draggingSpectrum)
        {
            return;
        }

        UpdateSpectrum(e.GetPosition(SpectrumSurface));
        _draggingSpectrum = false;
        SpectrumSurface.ReleaseMouseCapture();
    }

    private void OnHueMouseDown(object sender, MouseButtonEventArgs e)
    {
        _draggingHue = true;
        HueSurface.CaptureMouse();
        UpdateHue(e.GetPosition(HueSurface));
    }

    private void OnHueMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_draggingHue && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateHue(e.GetPosition(HueSurface));
        }
    }

    private void OnHueMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_draggingHue)
        {
            return;
        }

        UpdateHue(e.GetPosition(HueSurface));
        _draggingHue = false;
        HueSurface.ReleaseMouseCapture();
    }

    private void OnPickerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ReferenceEquals(sender, SpectrumSurface))
        {
            ApplyRoundedClip(SpectrumSurface, 10);
        }

        UpdateThumbPositions();
    }

    private void OnPanelChromeSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyRoundedClip(PickerChrome, 12);

    private static void ApplyRoundedClip(FrameworkElement element, double radius)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return;
        }

        element.Clip = new RectangleGeometry(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight),
            radius,
            radius);
    }

    private void OnHexTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingFields || !ColorMath.TryParseHex(HexBox.Text, out MediaColor color))
        {
            return;
        }

        SetSelectedColor(color);
    }

    private void OnRgbTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingFields ||
            !TryParseChannel(RedBox.Text, out byte red) ||
            !TryParseChannel(GreenBox.Text, out byte green) ||
            !TryParseChannel(BlueBox.Text, out byte blue))
        {
            return;
        }

        SetSelectedColor(MediaColor.FromRgb(red, green, blue));
    }

    private static bool TryParseChannel(string text, out byte value) =>
        byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private void OnColorFieldLostFocus(object sender, KeyboardFocusChangedEventArgs e) => UpdateColorFromHsv();

    private void OnPresetClicked(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: string value } && ColorMath.TryParseHex(value, out MediaColor color))
        {
            SetSelectedColor(color);
        }
    }

    private void OnPanelPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        Canceled?.Invoke(this, EventArgs.Empty);
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnConfirmClicked(object sender, RoutedEventArgs e) => Confirmed?.Invoke(this, EventArgs.Empty);

    private void OnCancelClicked(object sender, RoutedEventArgs e) => Canceled?.Invoke(this, EventArgs.Empty);
}
