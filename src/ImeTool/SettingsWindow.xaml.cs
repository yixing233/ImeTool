using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImeTool.Overlay;
using ImeTool.Caret;
using ImeTool.Hotkeys;
using ImeTool.Settings;
using ImeTool.State;
using ImeTool.Ime;
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

internal sealed record WindowMemoryListItem(
    WindowKey Key,
    string Title,
    string ProcessText,
    bool Enabled,
    string StatusText,
    Brush StatusBrush,
    string LastActiveText)
{
    public static WindowMemoryListItem From(
        WindowMemoryEntry entry,
        bool globalEnabled,
        DateTimeOffset now)
    {
        (string statusText, MediaColor statusColor) = !globalEnabled
            ? ("全局已关闭", MediaColor.FromRgb(0x78, 0x78, 0x78))
            : !entry.Enabled
                ? ("已暂停", MediaColor.FromRgb(0x78, 0x78, 0x78))
                : entry.Status switch
                {
                    ImeOpenStatus.Open => ("中文", MediaColor.FromRgb(0xD1, 0x34, 0x38)),
                    ImeOpenStatus.Closed => ("英文", MediaColor.FromRgb(0x0F, 0x6C, 0xBD)),
                    _ => ("等待状态", MediaColor.FromRgb(0x78, 0x78, 0x78))
                };

        TimeSpan age = now - entry.LastActivatedAt;
        string lastActiveText = age < TimeSpan.FromMinutes(1)
            ? "刚刚激活"
            : entry.LastActivatedAt.Date == now.Date
                ? $"{entry.LastActivatedAt:HH:mm:ss} 激活"
                : $"{entry.LastActivatedAt:MM-dd HH:mm} 激活";
        string processText = entry.ProcessName.StartsWith("PID ", StringComparison.OrdinalIgnoreCase)
            ? entry.ProcessName
            : $"{entry.ProcessName}.exe";

        return new WindowMemoryListItem(
            entry.Key,
            entry.Title,
            processText,
            entry.Enabled,
            statusText,
            new SolidColorBrush(statusColor),
            lastActiveText);
    }
}

internal sealed record ImeDetectionRuleListItem(ImeDetectionRule Rule)
{
    public string SignalText => $"{Rule.KeyboardLayout} · open={Rule.OpenStatusCode?.ToString() ?? "*"} · conversion={Rule.ConversionMode?.ToString() ?? "*"}";
    public string DetailText => "匹配键盘布局和输入法原始状态码";
    public string ResultText => Rule.Result == TextInputMode.Chinese ? "中文" : "英文";
}

internal sealed record ApplicationRuleListItem(ApplicationRule Rule)
{
    public string MatchText
    {
        get
        {
            var parts = new List<string> { $"{Rule.ProcessName}.exe" };
            if (!string.IsNullOrEmpty(Rule.WindowTitleContains)) parts.Add($"标题含“{Rule.WindowTitleContains}”");
            if (!string.IsNullOrEmpty(Rule.WindowClass)) parts.Add($"窗口类 {Rule.WindowClass}");
            if (!string.IsNullOrEmpty(Rule.ControlClass)) parts.Add($"控件类 {Rule.ControlClass}");
            return string.Join(" · ", parts);
        }
    }

    public string ActionText
    {
        get
        {
            var actions = new List<string>();
            if (Rule.HideMarker) actions.Add("隐藏标记");
            if (Rule.DisableWindowMemory) actions.Add("禁用记忆");
            if (Rule.DisableStateRestore) actions.Add("禁止恢复");
            return actions.Count == 0 ? "仅调整位置" : string.Join(" · ", actions);
        }
    }

    public string OffsetText => Rule.OffsetX is null && Rule.OffsetY is null
        ? string.Empty
        : $"偏移 {Rule.OffsetX?.ToString() ?? "—"}, {Rule.OffsetY?.ToString() ?? "—"}";
}

public partial class SettingsWindow : FluentWindow
{
    private bool _isInitialized;
    private readonly Dictionary<string, HotkeyGestureSettings?> _hotkeyBindings = new(StringComparer.Ordinal);
    private string? _capturingHotkeyName;
    private MarkerState _previewState = MarkerState.Chinese;
    private readonly Dictionary<MarkerState, StateAppearanceDraft> _stateDrafts = new();
    private readonly WindowDiscoveryService _windowDiscoveryService = new();
    private readonly IWindowMemorySource? _windowMemorySource;
    private readonly IImeDiagnosticsSource? _imeDiagnosticsSource;
    private readonly List<ImeDetectionRule> _imeDetectionRules = [];
    private readonly List<ApplicationRule> _advancedApplicationRules = [];
    private readonly GitHubUpdateService _updateService = new();
    private readonly CancellationTokenSource _updateCancellation = new();
    private MarkerState _stateEditorState = MarkerState.Chinese;
    private UpdateRelease? _availableUpdate;
    private UpdateRelease? _displayedRelease;
    private bool _windowClosed;
    private bool _updateCheckInProgress;
    private bool _updatingStateEditor;

    public SettingsWindow(
        AppSettings settings,
        IWindowMemorySource? windowMemorySource = null,
        IImeDiagnosticsSource? imeDiagnosticsSource = null)
    {
        InitializeComponent();
        SettingsScroll.Resources[SystemParameters.VerticalScrollBarWidthKey] = 8d;

        _windowMemorySource = windowMemorySource;
        _imeDiagnosticsSource = imeDiagnosticsSource;
        if (_windowMemorySource is not null)
        {
            _windowMemorySource.EntriesChanged += OnWindowMemoryEntriesChanged;
        }
        if (_imeDiagnosticsSource is not null)
        {
            _imeDiagnosticsSource.SnapshotChanged += OnImeDiagnosticsChanged;
        }

        Settings = settings.Normalize();
        InitializeStyleBox();
        InitializeBackdropBox();
        InitializeDisplayModeBox();
        InitializeCaretCaptureModeBox();
        LoadFromSettings(Settings);
        _isInitialized = true;
        UpdateLivePreview();
        RefreshWindowMemory();
        RefreshImeDiagnostics();
        ShowSettingsPage(Math.Max(0, SettingsNavigation.SelectedIndex));
        Loaded += OnSettingsWindowLoaded;
    }

    public AppSettings Settings { get; private set; }
    public event Action<AppSettings>? SettingsSaved;

    protected override void OnClosed(EventArgs e)
    {
        _windowClosed = true;
        Loaded -= OnSettingsWindowLoaded;
        if (_windowMemorySource is not null)
        {
            _windowMemorySource.EntriesChanged -= OnWindowMemoryEntriesChanged;
        }
        if (_imeDiagnosticsSource is not null)
        {
            _imeDiagnosticsSource.SnapshotChanged -= OnImeDiagnosticsChanged;
        }

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

    private void InitializeCaretCaptureModeBox()
    {
        CaretCaptureModeBox.Items.Add(CreateCaretCaptureModeItem(
            "自动（推荐）",
            CaretCaptureMode.Automatic,
            "融合 Win32 与 UI Automation，并过滤非输入区域的过期光标。"));
        CaretCaptureModeBox.Items.Add(CreateCaretCaptureModeItem(
            "Win32 Caret",
            CaretCaptureMode.Win32,
            "仅使用 GetGUIThreadInfo，适合传统桌面输入框。"));
        CaretCaptureModeBox.Items.Add(CreateCaretCaptureModeItem(
            "UI Automation",
            CaretCaptureMode.UiAutomation,
            "仅使用 UI Automation，适合浏览器及现代应用输入框。"));
        CaretCaptureModeBox.Items.Add(CreateCaretCaptureModeItem(
            "MSAA",
            CaretCaptureMode.Msaa,
            "通过 Microsoft Active Accessibility 的 OBJID_CARET 获取光标。"));
        CaretCaptureModeBox.Items.Add(CreateCaretCaptureModeItem(
            "浏览器兼容",
            CaretCaptureMode.BrowserCompatibility,
            "针对 Chromium 与 Firefox 组合 UI Automation、MSAA 和 Win32 光标。"));
        CaretCaptureModeBox.Items.Add(CreateCaretCaptureModeItem(
            "JetBrains/JAB",
            CaretCaptureMode.JavaAccessBridge,
            "通过 Java Access Bridge 获取 JetBrains 与其他 Java 编辑器光标。"));
    }

    private static ComboBoxItem CreateCaretCaptureModeItem(
        string text,
        CaretCaptureMode mode,
        string toolTip) => new()
    {
        Content = text,
        Tag = mode,
        ToolTip = toolTip
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
        WindowMemoryEnabledBox.IsChecked = normalized.EnableWindowMemory;
        PersistWindowMemoryBox.IsChecked = normalized.PersistWindowMemory;
        WindowMemoryStoragePathBox.Text = ResolveWindowMemoryStoragePathForDisplay(normalized.WindowMemoryStoragePath);
        UpdateWindowMemoryPersistenceControls();
        UpdateStatusText.Text = $"当前版本 v{AppVersion.Display} · Windows x64";
        UpdateActionButton.Content = "检查更新";
        _availableUpdate = null;
        HideReleaseNotes();
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
        SelectCaretCaptureMode(normalized.CaretCaptureMode);
        AutoHideDelayBox.Text = normalized.MarkerBehavior.AutoHideDelayMilliseconds.ToString(CultureInfo.InvariantCulture);
        MotionBox.IsChecked = normalized.MarkerBehavior.EnableMotion;
        FollowAnimationDurationBox.Text = normalized.MarkerBehavior.FollowAnimationDurationMilliseconds.ToString(CultureInfo.InvariantCulture);
        FadeAnimationBox.IsChecked = normalized.MarkerBehavior.EnableFadeAnimation;
        GlobalHotkeysBox.IsChecked = normalized.Hotkeys.Enabled;
        LoadHotkeyBindings(normalized.Hotkeys);
        IReadOnlyList<ApplicationRule> processOnlyRules = normalized.ApplicationRules
            .Where(IsSimpleProcessRule)
            .ToArray();
        ExcludedProcessNamesBox.Text = JoinProcessNames(processOnlyRules.Where(IsCompleteExclusionRule));
        NoRestoreProcessNamesBox.Text = JoinProcessNames(processOnlyRules.Where(IsSimpleNoRestoreRule));
        _advancedApplicationRules.Clear();
        _advancedApplicationRules.AddRange(normalized.ApplicationRules.Where(rule =>
            !IsCompleteExclusionRule(rule) && !IsSimpleNoRestoreRule(rule)));
        RefreshAdvancedApplicationRules();
        _imeDetectionRules.Clear();
        _imeDetectionRules.AddRange(normalized.ImeDetectionRules);
        RefreshImeDetectionRuleList();
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

    private void SelectCaretCaptureMode(CaretCaptureMode mode)
    {
        foreach (ComboBoxItem item in CaretCaptureModeBox.Items)
        {
            if (item.Tag is CaretCaptureMode itemMode && itemMode == mode)
            {
                CaretCaptureModeBox.SelectedItem = item;
                return;
            }
        }

        CaretCaptureModeBox.SelectedIndex = 0;
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
        SetSectionVisibility(WindowMemorySectionTitle, WindowMemorySectionGroup, pageIndex == 2);
        SetSectionVisibility(WindowMemoryListSectionTitle, WindowMemoryListSectionGroup, pageIndex == 2);
        SetSectionVisibility(HotkeysSectionTitle, HotkeysSectionGroup, pageIndex == 3);
        SetSectionVisibility(ImeDiagnosticsSectionTitle, ImeDiagnosticsSectionGroup, pageIndex == 4);
        SetSectionVisibility(ApplicationRulesSectionTitle, ApplicationRulesSectionGroup, pageIndex == 5);
        if (pageIndex == 2)
        {
            RefreshWindowMemory();
        }

        if (pageIndex == 4)
        {
            RefreshImeDiagnostics();
        }

        if (pageIndex == 5)
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
        SettingsSaved?.Invoke(settings);
    }

    private bool TryBuildSettings(out AppSettings settings, out string? validationError)
    {
        GlobalHotkeySettings hotkeys = ReadHotkeySettings().Normalize();
        if (!TryValidateHotkeys(hotkeys, out validationError))
        {
            settings = Settings;
            return false;
        }

        string windowMemoryStoragePath;
        try
        {
            windowMemoryStoragePath = WindowMemoryPersistenceStore.ResolvePath(WindowMemoryStoragePathBox.Text);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            settings = Settings;
            validationError = "窗口记忆存储路径无效";
            return false;
        }

        if (PersistWindowMemoryBox.IsChecked == true)
        {
            var persistenceStore = new WindowMemoryPersistenceStore(windowMemoryStoragePath);
            if (!persistenceStore.TryProbeWrite(out string? persistenceError))
            {
                settings = Settings;
                validationError = $"窗口记忆存储路径不可写：{persistenceError}";
                return false;
            }
        }

        settings = new AppSettings
        {
            Enabled = EnabledBox.IsChecked == true,
            StartWithWindows = StartupBox.IsChecked == true,
            SilentStart = SilentStartBox.IsChecked == true,
            AutoCheckForUpdates = AutoCheckUpdatesBox.IsChecked == true,
            EnableWindowMemory = WindowMemoryEnabledBox.IsChecked == true,
            PersistWindowMemory = PersistWindowMemoryBox.IsChecked == true,
            WindowMemoryStoragePath = windowMemoryStoragePath,
            SettingsBackdrop = SelectedBackdrop(),
            Marker = ReadMarkerSettings(),
            MarkerBehavior = ReadMarkerBehaviorSettings(),
            CaretCaptureMode = SelectedCaretCaptureMode(),
            GlobalHotkeysEnabled = hotkeys.Enabled,
            Hotkeys = hotkeys,
            ApplicationRules = ReadApplicationRules(),
            ImeDetectionRules = ImeDetectionRuleNormalizer.Normalize(_imeDetectionRules)
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

        await CheckForUpdatesAsync();
    }

    private async void OnSettingsWindowLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnSettingsWindowLoaded;
        if (Settings.AutoCheckForUpdates)
        {
            await CheckForUpdatesAsync();
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_updateCheckInProgress || _windowClosed)
        {
            return;
        }

        _updateCheckInProgress = true;
        UpdateActionButton.IsEnabled = false;
        UpdateActionButton.Content = "检查中…";
        UpdateStatusText.Text = "正在连接 GitHub Releases…";
        HideReleaseNotes();
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
                ShowReleaseNotes(result.Release);
            }
            else if (result.Availability == UpdateAvailability.NoPublishedRelease)
            {
                UpdateStatusText.Text = $"当前版本 v{AppVersion.Display}，仓库暂未发布 Release";
                UpdateActionButton.Content = "重新检查";
                HideReleaseNotes();
            }
            else
            {
                UpdateStatusText.Text = $"当前已是最新版本 v{AppVersion.Display}";
                UpdateActionButton.Content = "重新检查";
                ShowReleaseNotes(result.Release);
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
            HideReleaseNotes();
        }
        finally
        {
            _updateCheckInProgress = false;
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
            UpdateStatusText.Text = "当前系统不支持自动运行更新安装包";
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
        UnauthorizedAccessException => "没有运行更新安装包的权限",
        _ => exception.Message
    };

    private void OnRefreshWindowsClicked(object sender, RoutedEventArgs e) => RefreshDetectedWindows();

    private void OnRefreshImeDiagnosticsClicked(object sender, RoutedEventArgs e) => RefreshImeDiagnostics();

    private void OnImeDiagnosticsChanged(ImeDiagnosticSnapshot snapshot)
    {
        if (_windowClosed)
        {
            return;
        }

        Dispatcher.BeginInvoke(() => DisplayImeDiagnostics(snapshot));
    }

    private void RefreshImeDiagnostics()
    {
        if (_imeDiagnosticsSource?.CurrentSnapshot is ImeDiagnosticSnapshot snapshot)
        {
            DisplayImeDiagnostics(snapshot);
            return;
        }

        ImeDiagnosticHintText.Text = "请先切换到任意文本输入框，再返回此页面";
    }

    private void DisplayImeDiagnostics(ImeDiagnosticSnapshot snapshot)
    {
        ImeDiagnosticHintText.Text = snapshot.MatchedRuleDescription is null
            ? "正在显示最近一次外部文本输入焦点的数据"
            : $"已匹配自定义规则：{snapshot.MatchedRuleDescription}";
        ImeFinalModeText.Text = FormatInputMode(snapshot.FinalMode);
        ImeDetectionSourceText.Text = snapshot.Source switch
        {
            ImeDetectionSource.CustomRule => "自定义状态码规则",
            ImeDetectionSource.ConversionMode => "Conversion Mode",
            ImeDetectionSource.OpenStatus => "Open Status",
            ImeDetectionSource.Context => "IMM Context",
            ImeDetectionSource.NonChineseLayout => "非中文键盘布局",
            ImeDetectionSource.Fallback => "兼容回退",
            _ => "等待数据"
        };
        ImeKeyboardLayoutText.Text = snapshot.KeyboardLayout;
        ImeLanguageIdText.Text = $"0x{snapshot.LanguageId:X4}";
        ImeOpenStatusCodeText.Text = snapshot.OpenStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "不可读";
        ImeConversionModeText.Text = snapshot.ConversionMode?.ToString(CultureInfo.InvariantCulture) ?? "不可读";
        ImeFocusText.Text = $"0x{snapshot.FocusHwnd.ToInt64():X} · {snapshot.CaretSource}";
        ImeTargetWindowText.Text = string.IsNullOrWhiteSpace(snapshot.WindowTitle)
            ? snapshot.ProcessName
            : $"{snapshot.ProcessName} · {snapshot.WindowTitle}";
        ImeClassInfoText.Text = $"窗口类：{EmptyDisplay(snapshot.WindowClass)} · 控件类：{EmptyDisplay(snapshot.ControlClass)}";
    }

    private void OnCaptureChineseImeRuleClicked(object sender, RoutedEventArgs e) =>
        CaptureCurrentImeRule(TextInputMode.Chinese);

    private void OnCaptureEnglishImeRuleClicked(object sender, RoutedEventArgs e) =>
        CaptureCurrentImeRule(TextInputMode.English);

    private void CaptureCurrentImeRule(TextInputMode result)
    {
        if (_imeDiagnosticsSource?.CurrentSnapshot is not ImeDiagnosticSnapshot snapshot ||
            (!snapshot.OpenStatusCode.HasValue && !snapshot.ConversionMode.HasValue))
        {
            ImeDiagnosticHintText.Text = "当前输入框没有可用于创建规则的状态码";
            return;
        }

        var rule = new ImeDetectionRule
        {
            KeyboardLayout = snapshot.KeyboardLayout,
            OpenStatusCode = snapshot.OpenStatusCode,
            ConversionMode = snapshot.ConversionMode,
            Result = result
        };
        _imeDetectionRules.RemoveAll(existing =>
            string.Equals(existing.KeyboardLayout, rule.KeyboardLayout, StringComparison.OrdinalIgnoreCase) &&
            existing.OpenStatusCode == rule.OpenStatusCode &&
            existing.ConversionMode == rule.ConversionMode);
        _imeDetectionRules.Add(rule);
        RefreshImeDetectionRuleList();
        ImeDiagnosticHintText.Text = $"已将当前状态码记录为{FormatInputMode(result)}，保存设置后生效";
    }

    private void OnDeleteImeRuleClicked(object sender, RoutedEventArgs e)
    {
        if (ImeDetectionRulesList.SelectedItem is not ImeDetectionRuleListItem item)
        {
            return;
        }

        _imeDetectionRules.Remove(item.Rule);
        RefreshImeDetectionRuleList();
    }

    private void RefreshImeDetectionRuleList()
    {
        ImeDetectionRulesList.ItemsSource = _imeDetectionRules
            .Select(rule => new ImeDetectionRuleListItem(rule))
            .ToArray();
    }

    private static string FormatInputMode(TextInputMode mode) => mode switch
    {
        TextInputMode.Chinese => "中文",
        TextInputMode.English => "英文",
        _ => "未知"
    };

    private static string EmptyDisplay(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;

    private void OnWindowMemoryGlobalChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            RefreshWindowMemory();
        }
    }

    private void ShowReleaseNotes(UpdateRelease? release)
    {
        if (release is null)
        {
            HideReleaseNotes();
            return;
        }

        _displayedRelease = release;
        UpdateNotesTitleText.Text = $"v{AppVersion.Format(release.Version)} 更新日志";
        UpdateNotesDateText.Text = release.PublishedAt is DateTimeOffset publishedAt
            ? $"发布于 {publishedAt.ToLocalTime():yyyy-MM-dd HH:mm}"
            : "GitHub Release";
        UpdateNotesText.Text = string.IsNullOrWhiteSpace(release.ReleaseNotes)
            ? ReleaseNotesFormatter.FromMarkdown(null)
            : release.ReleaseNotes;
        UpdateNotesPanel.Visibility = Visibility.Visible;
    }

    private void HideReleaseNotes()
    {
        _displayedRelease = null;
        UpdateNotesPanel.Visibility = Visibility.Collapsed;
    }

    private void OnOpenReleasePageClicked(object sender, RoutedEventArgs e)
    {
        if (_displayedRelease is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_displayedRelease.ReleasePageUri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            UpdateStatusText.Text = $"打开发布页面失败：{exception.Message}";
        }
    }

    private void OnOpenReleaseHistoryClicked(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<ReleaseHistoryEntry> entries = ReleaseHistoryCatalog.LoadBundled();
        var window = new ReleaseHistoryWindow(entries, SelectedBackdrop())
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void OnWindowMemoryPersistenceChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            UpdateWindowMemoryPersistenceControls();
            RefreshWindowMemory();
        }
    }

    private void OnBrowseWindowMemoryStorageClicked(object sender, RoutedEventArgs e)
    {
        string currentPath = ResolveWindowMemoryStoragePathForDisplay(WindowMemoryStoragePathBox.Text);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "选择窗口记忆存储文件",
            Filter = "JSON 文件|*.json|所有文件|*.*",
            AddExtension = true,
            DefaultExt = ".json",
            FileName = Path.GetFileName(currentPath),
            InitialDirectory = Path.GetDirectoryName(currentPath),
            OverwritePrompt = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            WindowMemoryStoragePathBox.Text = dialog.FileName;
        }
    }

    private void OnResetWindowMemoryStorageClicked(object sender, RoutedEventArgs e) =>
        WindowMemoryStoragePathBox.Text = WindowMemoryPersistenceStore.DefaultPath;

    private void UpdateWindowMemoryPersistenceControls()
    {
        bool enabled = PersistWindowMemoryBox.IsChecked == true;
        WindowMemoryStoragePathBox.IsEnabled = enabled;
        BrowseWindowMemoryStorageButton.IsEnabled = enabled;
        ResetWindowMemoryStorageButton.IsEnabled = enabled;
    }

    private static string ResolveWindowMemoryStoragePathForDisplay(string? path)
    {
        try
        {
            return WindowMemoryPersistenceStore.ResolvePath(path);
        }
        catch
        {
            return WindowMemoryPersistenceStore.DefaultPath;
        }
    }

    private void OnRefreshWindowMemoryClicked(object sender, RoutedEventArgs e) => RefreshWindowMemory();

    private void OnWindowMemoryEntriesChanged(object? sender, EventArgs e)
    {
        if (_windowClosed)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(RefreshWindowMemory);
            return;
        }

        RefreshWindowMemory();
    }

    private void RefreshWindowMemory()
    {
        if (WindowMemoryItemsControl is null)
        {
            return;
        }

        bool globalEnabled = WindowMemoryEnabledBox.IsChecked == true;
        bool persistenceEnabled = PersistWindowMemoryBox.IsChecked == true;
        IReadOnlyList<WindowMemoryEntry> entries = _windowMemorySource?.GetEntries() ?? [];
        WindowMemoryListItem[] items = entries
            .Select(entry => WindowMemoryListItem.From(entry, globalEnabled, DateTimeOffset.Now))
            .ToArray();
        WindowMemoryItemsControl.ItemsSource = items;
        WindowMemoryItemsControl.IsEnabled = globalEnabled;
        WindowMemoryEmptyState.Visibility = items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (persistenceEnabled && !string.IsNullOrWhiteSpace(_windowMemorySource?.PersistenceError))
        {
            WindowMemoryCountText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0xC4, 0x2B, 0x1C));
            WindowMemoryCountText.Text = $"持久化写入失败：{_windowMemorySource.PersistenceError}";
        }
        else
        {
            WindowMemoryCountText.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x68, 0x68, 0x68));
            WindowMemoryCountText.Text = items.Length == 0
                ? (globalEnabled ? "尚未记录窗口" : "窗口记忆已关闭")
                : $"已记录 {items.Length} 个窗口 · {(persistenceEnabled ? "持久化已开启" : "仅本次运行")}";
        }
    }

    private void OnWindowMemoryItemToggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized ||
            _windowMemorySource is null ||
            sender is not Wpf.Ui.Controls.ToggleSwitch { Tag: WindowKey key } toggle)
        {
            return;
        }

        _windowMemorySource.SetWindowEnabled(key, toggle.IsChecked == true);
    }

    private void OnDetectedWindowSelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e) => UpdateDetectedWindowActions();

    private void OnAddDetectedWindowToExcludedClicked(object sender, RoutedEventArgs e) =>
        AddDetectedWindowToRule(ExcludedProcessNamesBox, "完全排除");

    private void OnAddDetectedWindowToNoRestoreClicked(object sender, RoutedEventArgs e) =>
        AddDetectedWindowToRule(NoRestoreProcessNamesBox, "不恢复状态");

    private void OnAddDetectedWindowAdvancedRuleClicked(object sender, RoutedEventArgs e)
    {
        if (DetectedWindowsBox.SelectedItem is not DetectedWindow window)
        {
            return;
        }

        var seed = new ApplicationRule
        {
            ProcessName = window.ProcessName,
            WindowTitleContains = window.Title,
            WindowClass = window.WindowClass,
            ControlClass = window.ControlClass
        };
        OpenApplicationRuleEditor(seed, editIndex: null, isNewRule: true);
    }

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
        AddDetectedWindowAdvancedRuleButton.IsEnabled = hasSelection;
    }

    private void OnAdvancedApplicationRuleSelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        bool hasSelection = AdvancedApplicationRulesBox.SelectedItem is ApplicationRuleListItem;
        EditAdvancedApplicationRuleButton.IsEnabled = hasSelection;
        DeleteAdvancedApplicationRuleButton.IsEnabled = hasSelection;
    }

    private void OnAdvancedApplicationRuleDoubleClicked(object sender, MouseButtonEventArgs e) =>
        EditSelectedAdvancedApplicationRule();

    private void OnNewAdvancedApplicationRuleClicked(object sender, RoutedEventArgs e) =>
        OpenApplicationRuleEditor(rule: null, editIndex: null, isNewRule: true);

    private void OnEditAdvancedApplicationRuleClicked(object sender, RoutedEventArgs e) =>
        EditSelectedAdvancedApplicationRule();

    private void OnDeleteAdvancedApplicationRuleClicked(object sender, RoutedEventArgs e)
    {
        int index = AdvancedApplicationRulesBox.SelectedIndex;
        if (index < 0 || index >= _advancedApplicationRules.Count)
        {
            return;
        }

        _advancedApplicationRules.RemoveAt(index);
        RefreshAdvancedApplicationRules();
    }

    private void EditSelectedAdvancedApplicationRule()
    {
        int index = AdvancedApplicationRulesBox.SelectedIndex;
        if (index < 0 || index >= _advancedApplicationRules.Count)
        {
            return;
        }

        OpenApplicationRuleEditor(_advancedApplicationRules[index], index, isNewRule: false);
    }

    private void OpenApplicationRuleEditor(ApplicationRule? rule, int? editIndex, bool isNewRule)
    {
        var editor = new ApplicationRuleEditorWindow(rule, SelectedBackdrop())
        {
            Owner = this
        };
        bool? result = editor.ShowDialog();
        if (result != true)
        {
            ApplyBackdrop(SelectedBackdrop());
            return;
        }

        if (!isNewRule && editIndex is int deleteIndex && editor.DeleteRequested)
        {
            _advancedApplicationRules.RemoveAt(deleteIndex);
        }
        else if (editor.Rule is ApplicationRule editedRule)
        {
            ApplicationRule normalized = ApplicationRuleNormalizer.Normalize([editedRule]).Single();
            if (editIndex is int index && !isNewRule)
            {
                _advancedApplicationRules[index] = normalized;
            }
            else
            {
                _advancedApplicationRules.Add(normalized);
            }
        }

        RefreshAdvancedApplicationRules();
        ApplyBackdrop(SelectedBackdrop());
    }

    private void RefreshAdvancedApplicationRules()
    {
        int selectedIndex = AdvancedApplicationRulesBox.SelectedIndex;
        AdvancedApplicationRulesBox.ItemsSource = _advancedApplicationRules
            .Select(rule => new ApplicationRuleListItem(rule))
            .ToArray();
        if (_advancedApplicationRules.Count > 0)
        {
            AdvancedApplicationRulesBox.SelectedIndex = Math.Clamp(selectedIndex, 0, _advancedApplicationRules.Count - 1);
        }
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
        var rules = new List<ApplicationRule>(_advancedApplicationRules);
        AddApplicationRules(rules, ExcludedProcessNamesBox.Text, completeExclusion: true);
        AddApplicationRules(rules, NoRestoreProcessNamesBox.Text, completeExclusion: false);
        return ApplicationRuleNormalizer.Normalize(rules);
    }

    private static void AddApplicationRules(
        List<ApplicationRule> rules,
        string text,
        bool completeExclusion)
    {
        foreach (string rawName in text.Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string processName = ApplicationRuleNormalizer.NormalizeProcessName(rawName);
            if (string.IsNullOrEmpty(processName))
            {
                continue;
            }

            rules.Add(new ApplicationRule
            {
                ProcessName = processName,
                HideMarker = completeExclusion,
                DisableWindowMemory = completeExclusion,
                DisableStateRestore = true
            });
        }
    }

    private static string JoinProcessNames(IEnumerable<ApplicationRule> rules) =>
        string.Join(Environment.NewLine, rules.Select(rule => rule.ProcessName));

    private static bool IsSimpleProcessRule(ApplicationRule rule) =>
        string.IsNullOrEmpty(rule.WindowTitleContains) &&
        string.IsNullOrEmpty(rule.WindowClass) &&
        string.IsNullOrEmpty(rule.ControlClass) &&
        rule.OffsetX is null && rule.OffsetY is null;

    private static bool IsCompleteExclusionRule(ApplicationRule rule) =>
        IsSimpleProcessRule(rule) && rule.HideMarker &&
        rule.DisableWindowMemory && rule.DisableStateRestore;

    private static bool IsSimpleNoRestoreRule(ApplicationRule rule) =>
        IsSimpleProcessRule(rule) && !rule.HideMarker &&
        !rule.DisableWindowMemory && rule.DisableStateRestore;

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

    private CaretCaptureMode SelectedCaretCaptureMode() =>
        CaretCaptureModeBox.SelectedItem is ComboBoxItem item && item.Tag is CaretCaptureMode mode
            ? mode
            : CaretCaptureMode.Automatic;

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
