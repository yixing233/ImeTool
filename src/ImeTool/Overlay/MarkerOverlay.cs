using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ImeTool.Native;
using ImeTool.Settings;
using ColorConverter = System.Windows.Media.ColorConverter;
using MediaColor = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Image = System.Windows.Controls.Image;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace ImeTool.Overlay;

public sealed class MarkerOverlay : Window
{
    private const int SafePaddingDip = 6;
    private IntPtr _hwnd;
    private MarkerState _lastStatus = MarkerState.Unknown;
    private MarkerAppearanceSettings? _lastSettings;
    private double _lastWidthDip;
    private double _lastHeightDip;
    private double _lastVisualOffsetDip;
    private bool _hasPosition;
    private bool _isPositionAnimating;
    private bool _renderingSubscribed;
    private double _currentLeft;
    private double _currentTop;
    private double _animationFromLeft;
    private double _animationFromTop;
    private double _targetLeft;
    private double _targetTop;
    private long _animationStartTimestamp;
    private double _animationDurationMilliseconds;
    private int _pixelWidth;
    private int _pixelHeight;
    private FrameworkElement? _visualHost;
    private bool _motionEnabled = true;
    private bool _fadeEnabled = true;
    private int _followAnimationDurationMilliseconds = 100;
    private bool _isFadeOutPending;
    private int _fadeGeneration;

    public MarkerOverlay()
    {
        Width = 24;
        Height = 24;
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
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) =>
        {
            StopPositionAnimation();
            CancelFadeAnimation();
        };
    }

    public void Update(
        MarkerState status,
        NativeMethods.RECT caretRect,
        MarkerAppearanceSettings settings,
        MarkerBehaviorSettings? behavior = null)
    {
        ConfigureBehavior(behavior ?? new MarkerBehaviorSettings());
        if (status == MarkerState.Unknown)
        {
            HideMarker();
            return;
        }

        bool shouldFadeIn = !IsVisible || _isFadeOutPending;
        CancelFadeAnimation();

        MarkerAppearanceSettings normalized = settings.Normalize();
        if (_lastStatus != status || _lastSettings != normalized || Content is null)
        {
            ApplyVisual(status, normalized);
            _lastStatus = status;
            _lastSettings = normalized;
        }

        bool wasVisibleAndPositioned = IsVisible && _hasPosition;
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

        (double dpiX, double dpiY) = GetDpiScale();
        _pixelWidth = Math.Max(1, (int)Math.Ceiling(_lastWidthDip * dpiX));
        _pixelHeight = Math.Max(1, (int)Math.Ceiling(_lastHeightDip * dpiY));
        int pixelOffsetX = (int)Math.Round(_lastVisualOffsetDip * dpiX);
        int pixelOffsetY = (int)Math.Round(_lastVisualOffsetDip * dpiY);
        int markerOffsetX = (int)Math.Round(normalized.OffsetX * dpiX);
        int markerOffsetY = (int)Math.Round(normalized.OffsetY * dpiY);
        MarkerPlacementResult placement = TryGetWorkArea(caretRect, out NativeMethods.RECT workArea)
            ? MarkerPlacement.Calculate(
                caretRect,
                _pixelWidth,
                _pixelHeight,
                pixelOffsetX,
                pixelOffsetY,
                markerOffsetX,
                markerOffsetY,
                workArea)
            : new MarkerPlacementResult(
                caretRect.Right + markerOffsetX - pixelOffsetX,
                caretRect.Bottom + markerOffsetY - pixelOffsetY,
                false,
                false);

        int targetLeft = placement.Left;
        double targetTop = placement.Top;
        if (wasVisibleAndPositioned)
        {
            double verticalTolerancePixels = Math.Ceiling(MarkerMotion.VerticalJitterToleranceDip * dpiY);
            targetTop = MarkerMotion.StabilizeTarget(_targetTop, targetTop, verticalTolerancePixels);
        }

        bool targetClearsCaret = MarkerPlacement.IsVisualClearOfCaret(
            targetLeft,
            targetTop,
            pixelOffsetX,
            pixelOffsetY,
            caretRect);
        bool currentClearsCaret = MarkerPlacement.IsVisualClearOfCaret(
            _currentLeft,
            _currentTop,
            pixelOffsetX,
            pixelOffsetY,
            caretRect);
        bool mustSnapForCaretClearance = targetClearsCaret && !currentClearsCaret;

        if (!wasVisibleAndPositioned ||
            !_motionEnabled ||
            mustSnapForCaretClearance ||
            !MarkerMotion.ShouldAnimate(_currentLeft, _currentTop, targetLeft, targetTop))
        {
            SnapToPosition(targetLeft, targetTop);
        }
        else
        {
            AnimateToPosition(targetLeft, targetTop);
        }

        if (shouldFadeIn)
        {
            StartFadeIn();
        }
    }

    public void ConfigureBehavior(MarkerBehaviorSettings behavior)
    {
        MarkerBehaviorSettings normalized = behavior.Normalize();
        bool systemAnimationsEnabled = SystemParameters.ClientAreaAnimation;
        _motionEnabled = normalized.EnableMotion && systemAnimationsEnabled;
        _fadeEnabled = normalized.EnableFadeAnimation && systemAnimationsEnabled;
        _followAnimationDurationMilliseconds = normalized.FollowAnimationDurationMilliseconds;
    }

    private static bool TryGetWorkArea(NativeMethods.RECT caretRect, out NativeMethods.RECT workArea)
    {
        workArea = default;
        var point = new NativeMethods.POINT
        {
            X = caretRect.Left,
            Y = caretRect.Bottom
        };
        IntPtr monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new NativeMethods.MONITORINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        workArea = monitorInfo.rcWork;
        return workArea.Right > workArea.Left && workArea.Bottom > workArea.Top;
    }

    public void HideMarker(bool immediate = false)
    {
        StopPositionAnimation();
        _hasPosition = false;
        if (!IsVisible || _isFadeOutPending)
        {
            return;
        }

        if (!immediate && _fadeEnabled && _visualHost is not null)
        {
            StartFadeOut();
        }
        else
        {
            CancelFadeAnimation();
            Hide();
        }
    }

    private void SnapToPosition(double left, double top)
    {
        StopPositionAnimation();
        _currentLeft = left;
        _currentTop = top;
        _targetLeft = left;
        _targetTop = top;
        _hasPosition = true;
        MoveNativeWindow(left, top);
    }

    private void AnimateToPosition(double left, double top)
    {
        long now = Stopwatch.GetTimestamp();
        if (_isPositionAnimating)
        {
            AdvancePositionAnimation(now, moveWindow: false);
        }

        double distance = MarkerMotion.Distance(_currentLeft, _currentTop, left, top);
        _animationFromLeft = _currentLeft;
        _animationFromTop = _currentTop;
        _targetLeft = left;
        _targetTop = top;
        _animationStartTimestamp = now;
        _animationDurationMilliseconds = MarkerMotion.DurationMilliseconds(
            distance,
            _followAnimationDurationMilliseconds);
        _isPositionAnimating = true;
        _hasPosition = true;
        SubscribeToRendering();
    }

    private void OnCompositionTargetRendering(object? sender, EventArgs e)
    {
        if (_isPositionAnimating)
        {
            AdvancePositionAnimation(Stopwatch.GetTimestamp(), moveWindow: true);
        }
    }

    private void AdvancePositionAnimation(long timestamp, bool moveWindow)
    {
        double elapsedMilliseconds = (timestamp - _animationStartTimestamp) * 1000d / Stopwatch.Frequency;
        double progress = _animationDurationMilliseconds <= 0 ? 1 : elapsedMilliseconds / _animationDurationMilliseconds;
        _currentLeft = MarkerMotion.Interpolate(_animationFromLeft, _targetLeft, progress);
        _currentTop = MarkerMotion.Interpolate(_animationFromTop, _targetTop, progress);

        if (moveWindow)
        {
            MoveNativeWindow(_currentLeft, _currentTop);
        }

        if (progress >= 1)
        {
            _currentLeft = _targetLeft;
            _currentTop = _targetTop;
            if (moveWindow)
            {
                MoveNativeWindow(_currentLeft, _currentTop);
            }

            StopPositionAnimation();
        }
    }

    private void MoveNativeWindow(double left, double top)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HwndTopmost,
            (int)Math.Round(left),
            (int)Math.Round(top),
            _pixelWidth,
            _pixelHeight,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    private void SubscribeToRendering()
    {
        if (_renderingSubscribed)
        {
            return;
        }

        CompositionTarget.Rendering += OnCompositionTargetRendering;
        _renderingSubscribed = true;
    }

    private void StopPositionAnimation()
    {
        _isPositionAnimating = false;
        if (!_renderingSubscribed)
        {
            return;
        }

        CompositionTarget.Rendering -= OnCompositionTargetRendering;
        _renderingSubscribed = false;
    }

    private void ApplyVisual(MarkerState status, MarkerAppearanceSettings settings)
    {
        FrameworkElement visual = settings.Style switch
        {
            MarkerStyle.Text => CreateTextMarker(status, settings),
            MarkerStyle.Image => CreateImageMarker(status, settings) ?? CreateDotMarker(status, settings),
            _ => CreateDotMarker(status, settings)
        };

        _lastVisualOffsetDip = SafePaddingDip;
        _lastWidthDip = Math.Max(1, Math.Ceiling(visual.Width + SafePaddingDip * 2));
        _lastHeightDip = Math.Max(1, Math.Ceiling(visual.Height + SafePaddingDip * 2));
        Width = _lastWidthDip;
        Height = _lastHeightDip;
        visual.Margin = new Thickness(SafePaddingDip);
        _visualHost = new Grid
        {
            Width = _lastWidthDip,
            Height = _lastHeightDip,
            ClipToBounds = false,
            Children = { visual }
        };
        Content = _visualHost;
    }

    private void StartFadeIn()
    {
        if (_visualHost is null)
        {
            return;
        }

        if (!_fadeEnabled)
        {
            _visualHost.Opacity = 1;
            return;
        }

        int generation = ++_fadeGeneration;
        _visualHost.BeginAnimation(OpacityProperty, null);
        _visualHost.Opacity = 0;
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(110))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            if (generation == _fadeGeneration && _visualHost is not null)
            {
                _visualHost.BeginAnimation(OpacityProperty, null);
                _visualHost.Opacity = 1;
            }
        };
        _visualHost.BeginAnimation(OpacityProperty, animation);
    }

    private void StartFadeOut()
    {
        if (_visualHost is null)
        {
            Hide();
            return;
        }

        _isFadeOutPending = true;
        int generation = ++_fadeGeneration;
        double currentOpacity = _visualHost.Opacity;
        _visualHost.BeginAnimation(OpacityProperty, null);
        _visualHost.Opacity = currentOpacity;
        var animation = new DoubleAnimation(currentOpacity, 0, TimeSpan.FromMilliseconds(90))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            if (generation != _fadeGeneration)
            {
                return;
            }

            _isFadeOutPending = false;
            if (_visualHost is not null)
            {
                _visualHost.BeginAnimation(OpacityProperty, null);
                _visualHost.Opacity = 1;
            }

            if (IsVisible)
            {
                Hide();
            }
        };
        _visualHost.BeginAnimation(OpacityProperty, animation);
    }

    private void CancelFadeAnimation()
    {
        _fadeGeneration++;
        _isFadeOutPending = false;
        if (_visualHost is not null)
        {
            _visualHost.BeginAnimation(OpacityProperty, null);
            _visualHost.Opacity = 1;
        }
    }

    private static FrameworkElement CreateDotMarker(MarkerState status, MarkerAppearanceSettings settings)
    {
        int size = Math.Clamp(settings.Size, 6, 96);
        return new Ellipse
        {
            Width = size,
            Height = size,
            StrokeThickness = 0,
            Fill = new SolidColorBrush(ParseColor(GetColor(status, settings)))
        };
    }

    private static FrameworkElement CreateTextMarker(MarkerState status, MarkerAppearanceSettings settings)
    {
        int size = Math.Clamp(settings.Size, 10, 96);
        string text = status switch
        {
            MarkerState.Chinese => settings.ChineseText,
            MarkerState.CapsLock => settings.CapsLockText,
            _ => settings.EnglishText
        };
        int textUnits = Math.Max(1, text.EnumerateRunes().Count());
        int horizontalPadding = Math.Max(8, (int)Math.Ceiling(size * 0.65));
        int width = Math.Max(size + horizontalPadding * 2, Math.Min(220, (int)Math.Ceiling(textUnits * size * 1.35 + horizontalPadding * 2)));
        int height = Math.Max(22, (int)Math.Ceiling(size * 1.55));

        return new Border
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(ParseColor(GetColor(status, settings))),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(220, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(height / 2.0),
            Padding = new Thickness(horizontalPadding, 0, horizontalPadding, 1),
            Child = new TextBlock
            {
                Text = text,
                Foreground = MediaBrushes.White,
                FontSize = Math.Max(9, size),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 1)
            }
        };
    }

    private static FrameworkElement? CreateImageMarker(MarkerState status, MarkerAppearanceSettings settings)
    {
        string? path = status switch
        {
            MarkerState.Chinese => settings.ChineseImagePath,
            MarkerState.CapsLock => settings.CapsLockImagePath,
            _ => settings.EnglishImagePath
        };
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            int size = Math.Clamp(settings.Size, 8, 96);
            return new Image
            {
                Width = size,
                Height = size,
                Source = bitmap,
                Stretch = Stretch.Uniform
            };
        }
        catch
        {
            return null;
        }
    }

    private static string GetColor(MarkerState status, MarkerAppearanceSettings settings) => status switch
    {
        MarkerState.Chinese => settings.ChineseColor,
        MarkerState.CapsLock => settings.CapsLockColor,
        _ => settings.EnglishColor
    };

    private static MediaColor ParseColor(string color)
    {
        try
        {
            object converted = ColorConverter.ConvertFromString(color) ?? MediaColor.FromRgb(128, 128, 128);
            return (MediaColor)converted;
        }
        catch
        {
            return MediaColor.FromRgb(128, 128, 128);
        }
    }

    private (double DpiX, double DpiY) GetDpiScale()
    {
        if (_hwnd != IntPtr.Zero && HwndSource.FromHwnd(_hwnd)?.CompositionTarget is { } target)
        {
            Matrix matrix = target.TransformToDevice;
            return (matrix.M11 <= 0 ? 1 : matrix.M11, matrix.M22 <= 0 ? 1 : matrix.M22);
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        return (dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX, dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        ApplyExtendedStyles();
    }

    private void ApplyExtendedStyles()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        IntPtr current = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GwlExStyle);
        long style = current.ToInt64() | NativeMethods.WsExTransparent | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GwlExStyle, new IntPtr(style));
    }
}
