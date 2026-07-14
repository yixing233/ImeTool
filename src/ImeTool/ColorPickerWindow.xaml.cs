using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using ImeTool.Settings;
using MediaColor = System.Windows.Media.Color;
using WpfBackdropType = Wpf.Ui.Controls.WindowBackdropType;

namespace ImeTool;

public partial class ColorPickerWindow : Wpf.Ui.Controls.FluentWindow
{
    private bool _isClosing;
    private bool _canClose;

    public ColorPickerWindow(MediaColor initialColor, SettingsWindowBackdrop backdrop)
    {
        InitializeComponent();
        WindowBackdropType = backdrop == SettingsWindowBackdrop.Acrylic
            ? WpfBackdropType.Acrylic
            : WpfBackdropType.Mica;

        Picker.ApplyBackdrop(backdrop);
        Picker.Open(initialColor);
        Picker.Confirmed += (_, _) => RequestClose(result: true);
        Picker.Canceled += (_, _) => RequestClose(result: false);
        Picker.DragRequested += (_, _) => TryDragWindow();
        Loaded += (_, _) =>
        {
            Diagnostics.DiagnosticsLog.Write($"Color picker shown with backdrop={backdrop}.");
            PlayOpenAnimation();
        };
    }

    public MediaColor SelectedColor => Picker.SelectedColor;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_canClose)
        {
            e.Cancel = true;
            RequestClose(result: false);
            return;
        }

        base.OnClosing(e);
    }

    private void PlayOpenAnimation()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            AnimatedRoot.Opacity = 1;
            AnimatedScale.ScaleX = 1;
            AnimatedScale.ScaleY = 1;
            AnimatedTranslate.Y = 0;
            return;
        }

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        AnimatedRoot.BeginAnimation(
            UIElement.OpacityProperty,
            CreateAnimation(0, 1, 180, easing));
        Animate(AnimatedScale, System.Windows.Media.ScaleTransform.ScaleXProperty, 0.965, 1, 200, easing);
        Animate(AnimatedScale, System.Windows.Media.ScaleTransform.ScaleYProperty, 0.965, 1, 200, easing);
        Animate(AnimatedTranslate, System.Windows.Media.TranslateTransform.YProperty, 10, 0, 200, easing);
    }

    private void RequestClose(bool result)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        Diagnostics.DiagnosticsLog.Write($"Color picker closing with result={result}.");
        Picker.IsHitTestVisible = false;

        if (!SystemParameters.ClientAreaAnimation)
        {
            CompleteClose(result);
            return;
        }

        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };
        var opacity = CreateAnimation(AnimatedRoot.Opacity, 0, 130, easing);
        opacity.Completed += (_, _) => CompleteClose(result);
        AnimatedRoot.BeginAnimation(UIElement.OpacityProperty, opacity);
        Animate(AnimatedScale, System.Windows.Media.ScaleTransform.ScaleXProperty, AnimatedScale.ScaleX, 0.975, 130, easing);
        Animate(AnimatedScale, System.Windows.Media.ScaleTransform.ScaleYProperty, AnimatedScale.ScaleY, 0.975, 130, easing);
        Animate(AnimatedTranslate, System.Windows.Media.TranslateTransform.YProperty, AnimatedTranslate.Y, 6, 130, easing);
    }

    private void CompleteClose(bool result)
    {
        _canClose = true;
        DialogResult = result;
    }

    private void TryDragWindow()
    {
        if (_isClosing)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The mouse may have been released between the routed event and DragMove.
        }
    }

    private static DoubleAnimation CreateAnimation(
        double from,
        double to,
        int durationMilliseconds,
        IEasingFunction easing) => new(from, to, TimeSpan.FromMilliseconds(durationMilliseconds))
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd
        };

    private static void Animate(
        System.Windows.Media.Animation.Animatable target,
        DependencyProperty property,
        double from,
        double to,
        int durationMilliseconds,
        IEasingFunction easing) =>
        target.BeginAnimation(property, CreateAnimation(from, to, durationMilliseconds, easing));
}
