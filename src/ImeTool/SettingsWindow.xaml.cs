using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImeTool.Overlay;
using ImeTool.Hotkeys;
using ImeTool.Settings;
using ImeTool.Updates;
using Border = System.Windows.Controls.Border;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using Grid = System.Windows.Controls.Grid;
using Image = System.Windows.Controls.Image;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using Ellipse = System.Windows.Shapes.Ellipse;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using ColorConverter = System.Windows.Media.ColorConverter;
using MediaColor = System.Windows.Media.Color;
using Key = System.Windows.Input.Key;
using Keyboard = System.Windows.Input.Keyboard;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using KeyInterop = System.Windows.Input.KeyInterop;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Button = System.Windows.Controls.Button;
using RadioButton = System.Windows.Controls.RadioButton;
using Canvas = System.Windows.Controls.Canvas;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;
using WpfBackdropType = Wpf.Ui.Controls.WindowBackdropType;

namespace ImeTool;

public partial class SettingsWindow : FluentWindow
{
    private bool _isInitialized;
    private readonly Dictionary<string, HotkeyGestureSettings?> _hotkeyBindings = new(StringComparer.Ordinal);
    private string? _capturingHotkeyName;
    private MarkerState _previewState = MarkerState.Chinese;
    private readonly Dictionary<MarkerState, StateAppearanceDraft> _stateDrafts = new();
    private readonly WindowDiscoveryService _windowDiscoveryService = new();
    private readonly GitHubUpdateService _updateService = new();
    private readonly CancellationTokenSource _updateCancellation = new();
    private MarkerState _stateEditorState = MarkerState.Chinese;
    private UpdateRelease? _availableUpdate;
    private bool _windowClosed;
    private bool _updatingStateEditor;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        SettingsScroll.Resources[SystemParameters.VerticalScrollBarWidthKey] = 8d;

        Settings = settings.Normalize();
        InitializeStyleBox();
        InitializeBackdropBox();
        InitializeDisplayModeBox();
        LoadFromSettings(Settings);
        _isInitialized = true;
        UpdateLivePreview();
        ShowSettingsPage(Math.Max(0, SettingsNavigation.SelectedIndex));
    }

    public AppSettings Settings { get; private set; }

    protected override void OnClosed(EventArgs e)
    {
        _windowClosed = true;
        _updateCancellation.Cancel();
        _updateService.Dispose();
        _updateCancellation.Dispose();
        base.OnClosed(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_capturingHotkeyName is not null)
            {
                _capturingHotkeyName = null;
                UpdateHotkeyDisplays();
            }

            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private void InitializeStyleBox()
    {
        StyleBox.Items.Add(CreateStyleItem("小圆点", MarkerStyle.Dot));
        StyleBox.Items.Add(CreateStyleItem("文字胶囊", MarkerStyle.Text));
        StyleBox.Items.Add(CreateStyleItem("自定义图片", MarkerStyle.Image));
    }

    private static ComboBoxItem CreateStyleItem(string text, MarkerStyle style) => new()
    {
        Content = text,
        Tag = style
    };

    private void InitializeBackdropBox()
    {
        BackdropBox.Items.Add(CreateBackdropItem("云母（Mica）", SettingsWindowBackdrop.Mica));
        BackdropBox.Items.Add(CreateBackdropItem("亚克力（Acrylic）", SettingsWindowBackdrop.Acrylic));
    }

    private static ComboBoxItem CreateBackdropItem(string text, SettingsWindowBackdrop backdrop) => new()
    {
        Content = text,
        Tag = backdrop
    };

    private void InitializeDisplayModeBox()
    {
        DisplayModeBox.Items.Add(CreateDisplayModeItem("始终显示", MarkerDisplayMode.Always));
        DisplayModeBox.Items.Add(CreateDisplayModeItem("切换中英文时", MarkerDisplayMode.OnImeChange));
        DisplayModeBox.Items.Add(CreateDisplayModeItem("输入或移动光标时", MarkerDisplayMode.WhileTyping));
    }

    private static ComboBoxItem CreateDisplayModeItem(string text, MarkerDisplayMode mode) => new()
    {
        Content = text,
        Tag = mode
    };

    private void LoadFromSettings(AppSettings settings)
    {
        bool updateAfterLoad = _isInitialized;
        _isInitialized = false;

        AppSettings normalized = settings.Normalize();
        EnabledBox.IsChecked = normalized.Enabled;
        StartupBox.IsChecked = normalized.StartWithWindows;
        SilentStartBox.IsChecked = normalized.SilentStart;
        AutoCheckUpdatesBox.IsChecked = normalized.AutoCheckForUpdates;
        UpdateStatusText.Text = $"当前版本 v{AppVersion.Display}";
        UpdateActionButton.Content = "检查更新";
        _availableUpdate = null;
        SelectBackdrop(normalized.SettingsBackdrop);
        ApplyBackdrop(normalized.SettingsBackdrop);
        SelectStyle(normalized.Marker.Style);
        SizeBox.Text = normalized.Marker.Size.ToString(CultureInfo.InvariantCulture);
        OffsetXBox.Text = normalized.Marker.OffsetX.ToString(CultureInfo.InvariantCulture);
        OffsetYBox.Text = normalized.Marker.OffsetY.ToString(CultureInfo.InvariantCulture);
        _stateDrafts[MarkerState.Chinese] = new StateAppearanceDraft(
            normalized.Marker.ChineseColor,
            normalized.Marker.ChineseText,
            normalized.Marker.ChineseImagePath);
        _stateDrafts[MarkerState.English] = new StateAppearanceDraft(
            normalized.Marker.EnglishColor,
            normalized.Marker.EnglishText,
            normalized.Marker.EnglishImagePath);
        _stateDrafts[MarkerState.CapsLock] = new StateAppearanceDraft(
            normalized.Marker.CapsLockColor,
            normalized.Marker.CapsLockText,
            normalized.Marker.CapsLockImagePath);
        _stateEditorState = MarkerState.Chinese;
        _previewState = MarkerState.Chinese;
        ChineseStateStyleTab.IsChecked = true;
        LoadStateEditor(_stateEditorState);
        SelectDisplayMode(normalized.MarkerBehavior.DisplayMode);
        AutoHideDelayBox.Text = normalized.MarkerBehavior.AutoHideDelayMilliseconds.ToString(CultureInfo.InvariantCulture);
        MotionBox.IsChecked = normalized.MarkerBehavior.EnableMotion;
        FollowAnimationDurationBox.Text = normalized.MarkerBehavior.FollowAnimationDurationMilliseconds.ToString(CultureInfo.InvariantCulture);
        FadeAnimationBox.IsChecked = normalized.MarkerBehavior.EnableFadeAnimation;
        GlobalHotkeysBox.IsChecked = normalized.Hotkeys.Enabled;
        LoadHotkeyBindings(normalized.Hotkeys);
        ExcludedProcessNamesBox.Text = JoinProcessNames(normalized.ApplicationRules.Where(rule => rule.Excluded));
        NoRestoreProcessNamesBox.Text = JoinProcessNames(normalized.ApplicationRules.Where(rule => rule.DisableStateRestore));
        SaveHintText.Text = string.Empty;

        _isInitialized = updateAfterLoad;
        if (updateAfterLoad)
        {
            UpdateLivePreview();
        }
    }

    private void SelectStyle(MarkerStyle style)
    {
        foreach (ComboBoxItem item in StyleBox.Items)
        {
            if (item.Tag is MarkerStyle itemStyle && itemStyle == style)
            {
                StyleBox.SelectedItem = item;
                return;
            }
        }

        StyleBox.SelectedIndex = 0;
    }

    private void SelectBackdrop(SettingsWindowBackdrop backdrop)
    {
        foreach (ComboBoxItem item in BackdropBox.Items)
        {
            if (item.Tag is SettingsWindowBackdrop itemBackdrop && itemBackdrop == backdrop)
            {
                BackdropBox.SelectedItem = item;
                return;
            }
        }

        BackdropBox.SelectedIndex = 0;
    }

    private void SelectDisplayMode(MarkerDisplayMode mode)
    {
        foreach (ComboBoxItem item in DisplayModeBox.Items)
        {
            if (item.Tag is MarkerDisplayMode itemMode && itemMode == mode)
            {
                DisplayModeBox.SelectedItem = item;
                return;
            }
        }

        DisplayModeBox.SelectedIndex = 0;
    }

    private void OnPreviewSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            UpdateLivePreview();
        }
    }

    private void OnStateStyleChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string stateName } ||
            !Enum.TryParse(stateName, out MarkerState state))
        {
            return;
        }

        if (!_isInitialized)
        {
            _stateEditorState = state;
            _previewState = state;
            return;
        }

        CaptureStateEditor();
        _stateEditorState = state;
        LoadStateEditor(state);
        SyncPreviewState(state);
        UpdateLivePreview();
    }

    private void OnStateEditorChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || _updatingStateEditor)
        {
            return;
        }

        CaptureStateEditor();
        UpdateLivePreview();
    }

    private void LoadStateEditor(MarkerState state)
    {
        StateAppearanceDraft draft = GetStateDraft(state);
        _updatingStateEditor = true;
        try
        {
            StateColorBox.Text = draft.Color;
            StateTextBox.Text = draft.Text;
            StateImagePathBox.Text = draft.ImagePath ?? string.Empty;

            (StateColorDescription.Text, StateTextDescription.Text, StateImageDescription.Text) = state switch
            {
                MarkerState.English => ("英文标记颜色", "英文文字胶囊内容", "英文自定义图片"),
                MarkerState.CapsLock => ("大写锁定标记颜色", "大写锁定文字胶囊内容", "大写锁定自定义图片"),
                _ => ("中文标记颜色", "中文文字胶囊内容", "中文自定义图片")
            };
            StateColorSwatch.Background = CreateBrush(draft.Color);
        }
        finally
        {
            _updatingStateEditor = false;
        }
    }

    private void CaptureStateEditor()
    {
        if (_updatingStateEditor)
        {
            return;
        }

        _stateDrafts[_stateEditorState] = new StateAppearanceDraft(
            StateColorBox.Text,
            StateTextBox.Text,
            EmptyToNull(StateImagePathBox.Text));
    }

    private StateAppearanceDraft GetStateDraft(MarkerState state)
    {
        if (_stateDrafts.TryGetValue(state, out StateAppearanceDraft? draft))
        {
            return draft;
        }

        return state switch
        {
            MarkerState.English => new StateAppearanceDraft(MarkerAppearanceSettings.DefaultEnglishColor, "英", null),
            MarkerState.CapsLock => new StateAppearanceDraft(MarkerAppearanceSettings.DefaultCapsLockColor, "A", null),
            _ => new StateAppearanceDraft(MarkerAppearanceSettings.DefaultChineseColor, "中", null)
        };
    }

    private void SyncPreviewState(MarkerState state)
    {
        _previewState = state;
    }

    private void OnSettingsNavigationChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitialized)
        {
            ShowSettingsPage(Math.Max(0, SettingsNavigation.SelectedIndex));
        }
    }

    private void ShowSettingsPage(int pageIndex)
    {
        SetSectionVisibility(GeneralSectionTitle, GeneralSectionGroup, pageIndex == 0);
        SetSectionVisibility(MarkerAppearanceSectionTitle, MarkerAppearanceSectionGroup, pageIndex == 1);
        SetSectionVisibility(DisplayBehaviorSectionTitle, DisplayBehaviorSectionGroup, pageIndex == 1);
        SetSectionVisibility(StateStyleSectionTitle, StateStyleSectionGroup, pageIndex == 1);
        SetSectionVisibility(PreviewSectionTitle, PreviewSectionGroup, pageIndex == 1);
        SetSectionVisibility(HotkeysSectionTitle, HotkeysSectionGroup, pageIndex == 2);
        SetSectionVisibility(ApplicationRulesSectionTitle, ApplicationRulesSectionGroup, pageIndex == 3);
        if (pageIndex == 3)
        {
            RefreshDetectedWindows();
        }

        SettingsScroll.ScrollToTop();
    }

    private static void SetSectionVisibility(FrameworkElement title, FrameworkElement content, bool visible)
    {
        Visibility visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        title.Visibility = visibility;
        content.Visibility = visibility;
    }

    private void OnBackdropChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        ApplyBackdrop(SelectedBackdrop());
    }

    private void ApplyBackdrop(SettingsWindowBackdrop backdrop)
    {
        bool acrylic = backdrop == SettingsWindowBackdrop.Acrylic;
        WindowBackdropType = acrylic ? WpfBackdropType.Acrylic : WpfBackdropType.Mica;

        // Acrylic needs lighter, more translucent surfaces so the backdrop remains visible.
        // Mica uses slightly denser surfaces to preserve hierarchy over its quieter texture.
        Resources["SettingsCardBackgroundBrush"] = CreateArgbBrush(acrylic ? 0x78 : 0xBF, 0xFF, 0xFF, 0xFF);
        Resources["SettingsCardBorderBrush"] = CreateArgbBrush(acrylic ? 0x70 : 0x80, 0xFF, 0xFF, 0xFF);
        Resources["SettingsDividerBrush"] = CreateArgbBrush(acrylic ? 0x32 : 0x3D, 0x70, 0x70, 0x70);
        Resources["SettingsFooterBackgroundBrush"] = CreateArgbBrush(acrylic ? 0x72 : 0xB8, 0xFA, 0xFA, 0xFA);
        Resources["SettingsPreviewBackgroundBrush"] = CreateArgbBrush(acrylic ? 0x58 : 0x8C, 0xFF, 0xFF, 0xFF);
        Resources["SettingsNavigationBackgroundBrush"] = CreateArgbBrush(acrylic ? 0x35 : 0x78, 0xFF, 0xFF, 0xFF);
    }

    private static SolidColorBrush CreateArgbBrush(int alpha, byte red, byte green, byte blue) =>
        new(MediaColor.FromArgb((byte)alpha, red, green, blue));

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (!TryBuildSettings(out AppSettings settings, out string? validationError))
        {
            SaveHintText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0xC4, 0x2B, 0x1C));
            SaveHintText.Text = validationError;
            return;
        }

        Settings = settings;
        SaveHintText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x0F, 0x7B, 0x0F));
        SaveHintText.Text = "设置已保存";
        DialogResult = true;
    }

    private bool TryBuildSettings(out AppSettings settings, out string? validationError)
    {
        GlobalHotkeySettings hotkeys = ReadHotkeySettings().Normalize();
        if (!TryValidateHotkeys(hotkeys, out validationError))
        {
            settings = Settings;
            return false;
        }

        settings = new AppSettings
        {
            Enabled = EnabledBox.IsChecked == true,
            StartWithWindows = StartupBox.IsChecked == true,
            SilentStart = SilentStartBox.IsChecked == true,
            AutoCheckForUpdates = AutoCheckUpdatesBox.IsChecked == true,
            SettingsBackdrop = SelectedBackdrop(),
            Marker = ReadMarkerSettings(),
            MarkerBehavior = ReadMarkerBehaviorSettings(),
            GlobalHotkeysEnabled = hotkeys.Enabled,
            Hotkeys = hotkeys,
            ApplicationRules = ReadApplicationRules()
        }.Normalize();
        return true;
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnResetClicked(object sender, RoutedEventArgs e) => LoadFromSettings(new AppSettings());

    private async void OnUpdateActionClicked(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is not null)
        {
            await DownloadAndInstallUpdateAsync(_availableUpdate);
            return;
        }

        UpdateActionButton.IsEnabled = false;
        UpdateActionButton.Content = "检查中…";
        UpdateStatusText.Text = "正在连接 GitHub Releases…";
        try
        {
            CancellationToken cancellationToken = _updateCancellation.Token;
            UpdateCheckResult result = await _updateService.CheckForUpdatesAsync(cancellationToken: cancellationToken);
            if (_windowClosed)
            {
                return;
            }

            if (result.Availability == UpdateAvailability.Available && result.Release is not null)
            {
                _availableUpdate = result.Release;
                UpdateStatusText.Text = $"发现新版本 v{AppVersion.Format(result.Release.Version)}";
                UpdateActionButton.Content = "下载并安装";
            }
            else if (result.Availability == UpdateAvailability.NoPublishedRelease)
            {
                UpdateStatusText.Text = $"当前版本 v{AppVersion.Display}，仓库暂未发布 Release";
                UpdateActionButton.Content = "重新检查";
            }
            else
            {
                UpdateStatusText.Text = $"当前已是最新版本 v{AppVersion.Display}";
                UpdateActionButton.Content = "重新检查";
            }
        }
        catch (OperationCanceledException) when (_windowClosed)
        {
            return;
        }
        catch (Exception exception)
        {
            UpdateStatusText.Text = $"检查失败：{GetUpdateErrorMessage(exception)}";
            UpdateActionButton.Content = "重试";
        }
        finally
        {
            if (!_windowClosed)
            {
                UpdateActionButton.IsEnabled = true;
            }
        }
    }

    private async Task DownloadAndInstallUpdateAsync(UpdateRelease release)
    {
        if (!SelfUpdateLauncher.CanInstall)
        {
            UpdateStatusText.Text = "当前运行方式不支持自动替换程序文件";
            return;
        }

        UpdateActionButton.IsEnabled = false;
        UpdateActionButton.Content = "下载中…";
        var progress = new Progress<double>(value =>
        {
            if (_windowClosed)
            {
                return;
            }

            int percentage = (int)Math.Round(value * 100);
            UpdateStatusText.Text = $"正在下载 v{AppVersion.Format(release.Version)}：{percentage}%";
        });

        try
        {
            CancellationToken cancellationToken = _updateCancellation.Token;
            string downloadedExecutable = await _updateService.DownloadUpdateAsync(
                release,
                progress,
                cancellationToken);
            if (_windowClosed || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            UpdateStatusText.Text = "校验完成，正在启动更新程序…";
            if (!TryBuildSettings(out AppSettings pendingSettings, out string? validationError))
            {
                UpdateStatusText.Text = $"无法安装：{validationError}";
                UpdateActionButton.Content = "重试安装";
                UpdateActionButton.IsEnabled = true;
                return;
            }

            new StartupManager().SetEnabled(pendingSettings.StartWithWindows);
            new SettingsService().Save(pendingSettings);
            Settings = pendingSettings;
            SelfUpdateLauncher.Launch(downloadedExecutable);
            System.Windows.Application.Current.Shutdown();
        }
        catch (OperationCanceledException) when (_windowClosed)
        {
            return;
        }
        catch (Exception exception)
        {
            UpdateStatusText.Text = $"更新失败：{GetUpdateErrorMessage(exception)}";
            UpdateActionButton.Content = "重试安装";
            UpdateActionButton.IsEnabled = true;
        }
    }

    private static string GetUpdateErrorMessage(Exception exception) => exception switch
    {
        HttpRequestException => "无法连接 GitHub",
        TaskCanceledException => "连接超时",
        InvalidDataException => exception.Message,
        UnauthorizedAccessException => "没有替换程序文件的权限",
        _ => exception.Message
    };

    private void OnRefreshWindowsClicked(object sender, RoutedEventArgs e) => RefreshDetectedWindows();

    private void OnDetectedWindowSelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e) => UpdateDetectedWindowActions();

    private void OnAddDetectedWindowToExcludedClicked(object sender, RoutedEventArgs e) =>
        AddDetectedWindowToRule(ExcludedProcessNamesBox, "完全排除");

    private void OnAddDetectedWindowToNoRestoreClicked(object sender, RoutedEventArgs e) =>
        AddDetectedWindowToRule(NoRestoreProcessNamesBox, "不恢复状态");

    private void RefreshDetectedWindows()
    {
        string? selectedProcessName = (DetectedWindowsBox.SelectedItem as DetectedWindow)?.ProcessName;
        IReadOnlyList<DetectedWindow> windows = _windowDiscoveryService.GetVisibleWindows();
        DetectedWindowsBox.ItemsSource = windows;

        DetectedWindow? selection = windows.FirstOrDefault(window =>
            string.Equals(window.ProcessName, selectedProcessName, StringComparison.OrdinalIgnoreCase));
        DetectedWindowsBox.SelectedItem = selection ?? windows.FirstOrDefault();
        DetectedWindowHintText.Text = windows.Count == 0 ? "未检测到其他可见窗口" : $"检测到 {windows.Count} 个窗口";
        UpdateDetectedWindowActions();
    }

    private void UpdateDetectedWindowActions()
    {
        bool hasSelection = DetectedWindowsBox.SelectedItem is DetectedWindow;
        AddDetectedWindowToExcludedButton.IsEnabled = hasSelection;
        AddDetectedWindowToNoRestoreButton.IsEnabled = hasSelection;
    }

    private void AddDetectedWindowToRule(TextBox target, string ruleName)
    {
        if (DetectedWindowsBox.SelectedItem is not DetectedWindow window)
        {
            return;
        }

        target.Text = ApplicationRuleTextEditor.AddProcessName(target.Text, window.ProcessName);
        DetectedWindowHintText.Text = $"已将 {window.ProcessName}.exe 加入“{ruleName}”";
    }

    private void OnRecordHotkeyClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name })
        {
            BeginHotkeyCapture(name);
        }
    }

    private void OnClearHotkeyClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name })
        {
            _hotkeyBindings[name] = null;
            _capturingHotkeyName = null;
            UpdateHotkeyDisplays();
        }
    }

    private void OnResetHotkeysClicked(object sender, RoutedEventArgs e)
    {
        LoadHotkeyBindings(new GlobalHotkeySettings { Enabled = GlobalHotkeysBox.IsChecked == true });
        _capturingHotkeyName = null;
        SaveHintText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x42, 0x42, 0x42));
        SaveHintText.Text = "快捷键已恢复默认，保存后生效";
    }

    private void OnHotkeyBoxMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox { Tag: string name })
        {
            BeginHotkeyCapture(name);
            e.Handled = true;
        }
    }

    private void OnHotkeyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { Tag: string name } box || _capturingHotkeyName != name)
        {
            return;
        }

        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            _capturingHotkeyName = null;
            UpdateHotkeyDisplays();
            return;
        }

        if (key is Key.Delete or Key.Back)
        {
            _hotkeyBindings[name] = null;
            _capturingHotkeyName = null;
            UpdateHotkeyDisplays();
            return;
        }

        if (IsModifierKey(key))
        {
            box.Text = "继续按下其他按键…";
            return;
        }

        HotkeyModifiers modifiers = ConvertModifiers(Keyboard.Modifiers);
        var gesture = new HotkeyGestureSettings
        {
            Modifiers = modifiers,
            VirtualKey = checked((uint)KeyInterop.VirtualKeyFromKey(key))
        };
        if (!gesture.IsValid)
        {
            box.Text = modifiers == HotkeyModifiers.None ? "需要至少一个修饰键" : "此按键不可用";
            return;
        }

        _hotkeyBindings[name] = gesture;
        _capturingHotkeyName = null;
        UpdateHotkeyDisplays();
    }

    private void OnHotkeyPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (sender is TextBox { Tag: string name } && _capturingHotkeyName == name)
        {
            e.Handled = true;
        }
    }

    private void BeginHotkeyCapture(string name)
    {
        TextBox? box = GetHotkeyBox(name);
        if (box is null)
        {
            return;
        }

        _capturingHotkeyName = name;
        box.Text = "请按新的组合键…";
        box.Focus();
        Keyboard.Focus(box);
    }

    private void LoadHotkeyBindings(GlobalHotkeySettings hotkeys)
    {
        _hotkeyBindings["ToggleEnabled"] = hotkeys.ToggleEnabled;
        _hotkeyBindings["ToggleMarkerVisibility"] = hotkeys.ToggleMarkerVisibility;
        _hotkeyBindings["OpenSettings"] = hotkeys.OpenSettings;
        _hotkeyBindings["ClearCurrentWindowState"] = hotkeys.ClearCurrentWindowState;
        UpdateHotkeyDisplays();
    }

    private void UpdateHotkeyDisplays()
    {
        SetHotkeyDisplay("ToggleEnabled");
        SetHotkeyDisplay("ToggleMarkerVisibility");
        SetHotkeyDisplay("OpenSettings");
        SetHotkeyDisplay("ClearCurrentWindowState");
    }

    private void SetHotkeyDisplay(string name)
    {
        if (GetHotkeyBox(name) is TextBox box)
        {
            _hotkeyBindings.TryGetValue(name, out HotkeyGestureSettings? gesture);
            box.Text = HotkeyGestureFormatter.Format(gesture);
        }
    }

    private TextBox? GetHotkeyBox(string name) => name switch
    {
        "ToggleEnabled" => ToggleEnabledHotkeyBox,
        "ToggleMarkerVisibility" => ToggleMarkerHotkeyBox,
        "OpenSettings" => OpenSettingsHotkeyBox,
        "ClearCurrentWindowState" => ClearWindowStateHotkeyBox,
        _ => null
    };

    private GlobalHotkeySettings ReadHotkeySettings() => new()
    {
        Enabled = GlobalHotkeysBox.IsChecked == true,
        ToggleEnabled = GetHotkeyBinding("ToggleEnabled"),
        ToggleMarkerVisibility = GetHotkeyBinding("ToggleMarkerVisibility"),
        OpenSettings = GetHotkeyBinding("OpenSettings"),
        ClearCurrentWindowState = GetHotkeyBinding("ClearCurrentWindowState")
    };

    private HotkeyGestureSettings? GetHotkeyBinding(string name) =>
        _hotkeyBindings.TryGetValue(name, out HotkeyGestureSettings? gesture) ? gesture : null;

    private static bool TryValidateHotkeys(GlobalHotkeySettings hotkeys, out string? error)
    {
        var seen = new Dictionary<HotkeyGestureSettings, string>();
        foreach ((string label, HotkeyGestureSettings? gesture) in EnumerateHotkeys(hotkeys))
        {
            if (gesture is null)
            {
                continue;
            }

            if (seen.TryGetValue(gesture, out string? existing))
            {
                error = $"“{existing}”与“{label}”不能使用相同快捷键";
                return false;
            }

            seen[gesture] = label;
        }

        error = null;
        return true;
    }

    private static IEnumerable<(string Label, HotkeyGestureSettings? Gesture)> EnumerateHotkeys(GlobalHotkeySettings hotkeys)
    {
        yield return ("启用或暂停", hotkeys.ToggleEnabled);
        yield return ("隐藏或显示标记", hotkeys.ToggleMarkerVisibility);
        yield return ("打开设置", hotkeys.OpenSettings);
        yield return ("清除当前窗口状态", hotkeys.ClearCurrentWindowState);
    }

    private static HotkeyModifiers ConvertModifiers(ModifierKeys modifiers)
    {
        HotkeyModifiers result = HotkeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= HotkeyModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= HotkeyModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= HotkeyModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= HotkeyModifiers.Windows;
        return result;
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin;

    private void OnPickStateColorClicked(object sender, RoutedEventArgs e) => PickColor(StateColorBox);

    private void OnBrowseStateImageClicked(object sender, RoutedEventArgs e) => BrowseImage(StateImagePathBox);

    private void OnClearStateImageClicked(object sender, RoutedEventArgs e) => StateImagePathBox.Text = string.Empty;

    private static void BrowseImage(TextBox target)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择标记图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.ico|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            target.Text = dialog.FileName;
        }
    }

    private void PickColor(TextBox target)
    {
        MediaColor initialColor = ColorMath.TryParseHex(target.Text, out MediaColor parsed)
            ? parsed
            : Colors.RoyalBlue;
        var dialog = new ColorPickerWindow(initialColor, SelectedBackdrop())
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            target.Text = ColorMath.ToHex(dialog.SelectedColor);
        }

        // Reapply the selected backdrop after the modal owner is activated again.
        ApplyBackdrop(SelectedBackdrop());
    }

    private void UpdateLivePreview()
    {
        MarkerAppearanceSettings marker = ReadMarkerSettings().Normalize();
        StateColorSwatch.Background = CreateBrush(GetStateDraft(_stateEditorState).Color);
        PreviewMarkerHost.Child = CreatePreviewContent(_previewState, marker);
        UpdatePreviewMarkerPosition(marker);

        string? previewImagePath = _previewState switch
        {
            MarkerState.Chinese => marker.ChineseImagePath,
            MarkerState.CapsLock => marker.CapsLockImagePath,
            _ => marker.EnglishImagePath
        };
        bool hasMissingImage = marker.Style == MarkerStyle.Image &&
                               !File.Exists(previewImagePath ?? string.Empty);
        PreviewHintText.Text = hasMissingImage ? "图片不存在时，实际显示会自动回退为圆点。" : string.Empty;
        PreviewHintText.Visibility = hasMissingImage ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePreviewMarkerPosition(MarkerAppearanceSettings marker)
    {
        const double caretLeft = 150;
        const double caretTop = 14;
        const double caretHeight = 24;
        Canvas.SetLeft(PreviewMarkerHost, caretLeft + marker.OffsetX);
        Canvas.SetTop(PreviewMarkerHost, caretTop + caretHeight + marker.OffsetY);
    }

    private static UIElement CreatePreviewContent(MarkerState status, MarkerAppearanceSettings marker)
    {
        string color = status switch
        {
            MarkerState.Chinese => marker.ChineseColor,
            MarkerState.CapsLock => marker.CapsLockColor,
            _ => marker.EnglishColor
        };
        string text = status switch
        {
            MarkerState.Chinese => marker.ChineseText,
            MarkerState.CapsLock => marker.CapsLockText,
            _ => marker.EnglishText
        };
        string? imagePath = status switch
        {
            MarkerState.Chinese => marker.ChineseImagePath,
            MarkerState.CapsLock => marker.CapsLockImagePath,
            _ => marker.EnglishImagePath
        };
        int size = Math.Clamp(marker.Size, 6, 96);
        MediaColor parsedColor = ParseMediaColor(color);

        if (marker.Style == MarkerStyle.Image && TryCreateImage(imagePath, size, out Image? image))
        {
            return Center(image!);
        }

        if (marker.Style == MarkerStyle.Text)
        {
            int height = Math.Max(24, (int)Math.Ceiling(size * 1.6));
            int padding = Math.Max(9, (int)Math.Ceiling(size * 0.7));
            return Center(new Border
            {
                Background = new SolidColorBrush(parsedColor),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(height / 2.0),
                Padding = new Thickness(padding, 0, padding, 1),
                MinWidth = height,
                Height = height,
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = Brushes.White,
                    FontSize = Math.Max(10, size),
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            });
        }

        return Center(new Ellipse
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(parsedColor),
            StrokeThickness = 0
        });
    }

    private static bool TryCreateImage(string? path, int size, out Image? image)
    {
        image = null;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.DecodePixelWidth = Math.Max(16, size * 2);
            bitmap.EndInit();
            bitmap.Freeze();

            image = new Image
            {
                Source = bitmap,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Grid Center(UIElement element)
    {
        var grid = new Grid();
        if (element is FrameworkElement frameworkElement)
        {
            frameworkElement.HorizontalAlignment = HorizontalAlignment.Center;
            frameworkElement.VerticalAlignment = VerticalAlignment.Center;
        }

        grid.Children.Add(element);
        return grid;
    }

    private MarkerAppearanceSettings ReadMarkerSettings()
    {
        CaptureStateEditor();
        StateAppearanceDraft chinese = GetStateDraft(MarkerState.Chinese);
        StateAppearanceDraft english = GetStateDraft(MarkerState.English);
        StateAppearanceDraft capsLock = GetStateDraft(MarkerState.CapsLock);

        return new MarkerAppearanceSettings
        {
            Style = SelectedStyle(),
            Size = ParseInt(SizeBox.Text, 12),
            OffsetX = ParseInt(OffsetXBox.Text, 6),
            OffsetY = ParseInt(OffsetYBox.Text, 6),
            ChineseColor = chinese.Color,
            EnglishColor = english.Color,
            CapsLockColor = capsLock.Color,
            ChineseText = chinese.Text,
            EnglishText = english.Text,
            CapsLockText = capsLock.Text,
            ChineseImagePath = chinese.ImagePath,
            EnglishImagePath = english.ImagePath,
            CapsLockImagePath = capsLock.ImagePath
        };
    }

    private MarkerBehaviorSettings ReadMarkerBehaviorSettings() => new()
    {
        DisplayMode = SelectedDisplayMode(),
        AutoHideDelayMilliseconds = ParseInt(AutoHideDelayBox.Text, 1500),
        EnableMotion = MotionBox.IsChecked == true,
        FollowAnimationDurationMilliseconds = ParseInt(FollowAnimationDurationBox.Text, 100),
        EnableFadeAnimation = FadeAnimationBox.IsChecked == true
    };

    private IReadOnlyList<ApplicationRule> ReadApplicationRules()
    {
        var rules = new Dictionary<string, ApplicationRule>(StringComparer.OrdinalIgnoreCase);
        AddApplicationRules(rules, ExcludedProcessNamesBox.Text, excluded: true, disableRestore: false);
        AddApplicationRules(rules, NoRestoreProcessNamesBox.Text, excluded: false, disableRestore: true);
        return ApplicationRuleNormalizer.Normalize(rules.Values.ToArray());
    }

    private static void AddApplicationRules(
        Dictionary<string, ApplicationRule> rules,
        string text,
        bool excluded,
        bool disableRestore)
    {
        foreach (string rawName in text.Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string processName = ApplicationRuleNormalizer.NormalizeProcessName(rawName);
            if (string.IsNullOrEmpty(processName))
            {
                continue;
            }

            rules.TryGetValue(processName, out ApplicationRule? existing);
            existing ??= new ApplicationRule { ProcessName = processName };
            rules[processName] = existing with
            {
                Excluded = existing.Excluded || excluded,
                DisableStateRestore = existing.DisableStateRestore || disableRestore
            };
        }
    }

    private static string JoinProcessNames(IEnumerable<ApplicationRule> rules) =>
        string.Join(Environment.NewLine, rules.Select(rule => rule.ProcessName));

    private MarkerStyle SelectedStyle() =>
        StyleBox.SelectedItem is ComboBoxItem item && item.Tag is MarkerStyle style
            ? style
            : MarkerStyle.Text;

    private SettingsWindowBackdrop SelectedBackdrop() =>
        BackdropBox.SelectedItem is ComboBoxItem item && item.Tag is SettingsWindowBackdrop backdrop
            ? backdrop
            : SettingsWindowBackdrop.Acrylic;

    private MarkerDisplayMode SelectedDisplayMode() =>
        DisplayModeBox.SelectedItem is ComboBoxItem item && item.Tag is MarkerDisplayMode mode
            ? mode
            : MarkerDisplayMode.Always;

    private static int ParseInt(string text, int fallback) => int.TryParse(text, out int value) ? value : fallback;

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Brush CreateBrush(string color)
    {
        try
        {
            return new SolidColorBrush((MediaColor)(ColorConverter.ConvertFromString(color) ?? Colors.Gray));
        }
        catch
        {
            return new SolidColorBrush(Colors.Gray);
        }
    }

    private static MediaColor ParseMediaColor(string color)
    {
        try
        {
            return (MediaColor)(ColorConverter.ConvertFromString(color) ?? Colors.Gray);
        }
        catch
        {
            return Colors.Gray;
        }
    }

    private sealed record StateAppearanceDraft(string Color, string Text, string? ImagePath);

}
