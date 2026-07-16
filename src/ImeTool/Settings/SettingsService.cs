using System.IO;
using System.Text.Json;
using ImeTool.Caret;
using ImeTool.Diagnostics;
using ImeTool.Ime;

namespace ImeTool.Settings;

public enum MarkerStyle
{
    Dot = 0,
    Text = 1,
    Image = 2
}

public enum SettingsWindowBackdrop
{
    Mica = 0,
    Acrylic = 1
}

public enum MarkerDisplayMode
{
    Always = 0,
    OnImeChange = 1,
    WhileTyping = 2
}

public sealed record ImeDetectionRule
{
    public string KeyboardLayout { get; init; } = "*";
    public long? OpenStatusCode { get; init; }
    public uint? ConversionMode { get; init; }
    public TextInputMode Result { get; init; }
}

public static class ImeDetectionRuleNormalizer
{
    public static IReadOnlyList<ImeDetectionRule> Normalize(IReadOnlyList<ImeDetectionRule>? rules)
    {
        if (rules is null || rules.Count == 0)
        {
            return [];
        }

        var normalized = new Dictionary<string, ImeDetectionRule>(StringComparer.OrdinalIgnoreCase);
        foreach (ImeDetectionRule? rule in rules)
        {
            if (rule is null ||
                rule.Result == TextInputMode.Unknown ||
                (!rule.OpenStatusCode.HasValue && !rule.ConversionMode.HasValue))
            {
                continue;
            }

            string layout = NormalizeKeyboardLayout(rule.KeyboardLayout);
            var value = rule with { KeyboardLayout = layout };
            string key = $"{layout}|{value.OpenStatusCode?.ToString() ?? "*"}|{value.ConversionMode?.ToString() ?? "*"}";
            normalized[key] = value;
        }

        return normalized.Values.ToArray();
    }

    public static string NormalizeKeyboardLayout(string? value)
    {
        string text = value?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text) || text == "*")
        {
            return "*";
        }

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return ulong.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out ulong layout)
            ? $"0x{layout:X16}"
            : "*";
    }
}

public sealed record MarkerBehaviorSettings
{
    public MarkerDisplayMode DisplayMode { get; init; } = MarkerDisplayMode.Always;
    public int AutoHideDelayMilliseconds { get; init; } = 1500;
    public bool EnableMotion { get; init; } = true;
    public int FollowAnimationDurationMilliseconds { get; init; } = 100;
    public bool EnableFadeAnimation { get; init; } = true;

    public MarkerBehaviorSettings Normalize() => this with
    {
        DisplayMode = Enum.IsDefined(DisplayMode) ? DisplayMode : MarkerDisplayMode.Always,
        AutoHideDelayMilliseconds = Math.Clamp(AutoHideDelayMilliseconds, 300, 10000),
        FollowAnimationDurationMilliseconds = Math.Clamp(FollowAnimationDurationMilliseconds, 40, 300)
    };
}

public sealed record AdditionalIndicatorSettings
{
    public bool EnableWindowBorder { get; init; }
    public int WindowBorderWidth { get; init; } = 3;
    public bool EnableMouseMarker { get; init; }
    public int MouseOffsetX { get; init; } = 14;
    public int MouseOffsetY { get; init; } = 18;
    public bool ColorizeIBeamCursor { get; init; }

    public AdditionalIndicatorSettings Normalize() => this with
    {
        WindowBorderWidth = Math.Clamp(WindowBorderWidth, 1, 12),
        MouseOffsetX = Math.Clamp(MouseOffsetX, -96, 96),
        MouseOffsetY = Math.Clamp(MouseOffsetY, -96, 96)
    };
}

public sealed record ApplicationRule
{
    public string ProcessName { get; init; } = string.Empty;
    public string WindowTitleContains { get; init; } = string.Empty;
    public string WindowClass { get; init; } = string.Empty;
    public string ControlClass { get; init; } = string.Empty;

    // Legacy settings used Excluded as one combined switch. It is retained for
    // JSON migration and expanded by ApplicationRuleNormalizer.
    public bool Excluded { get; init; }
    public bool HideMarker { get; init; }
    public bool DisableWindowMemory { get; init; }
    public bool DisableStateRestore { get; init; }
    public int? OffsetX { get; init; }
    public int? OffsetY { get; init; }
}

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008
}

public sealed record HotkeyGestureSettings
{
    public HotkeyModifiers Modifiers { get; init; }
    public uint VirtualKey { get; init; }

    public bool IsValid =>
        Modifiers != HotkeyModifiers.None &&
        (Modifiers & ~(HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Windows)) == 0 &&
        VirtualKey is >= 0x08 and <= 0xFE &&
        VirtualKey is not (0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5);
}

public sealed record GlobalHotkeySettings
{
    public bool Enabled { get; init; } = true;
    public HotkeyGestureSettings? ToggleEnabled { get; init; } = DefaultToggleEnabled;
    public HotkeyGestureSettings? ToggleMarkerVisibility { get; init; } = DefaultToggleMarkerVisibility;
    public HotkeyGestureSettings? OpenSettings { get; init; } = DefaultOpenSettings;
    public HotkeyGestureSettings? ClearCurrentWindowState { get; init; } = DefaultClearCurrentWindowState;

    public static HotkeyGestureSettings DefaultToggleEnabled => CreateDefault(0x49); // I
    public static HotkeyGestureSettings DefaultToggleMarkerVisibility => CreateDefault(0x48); // H
    public static HotkeyGestureSettings DefaultOpenSettings => CreateDefault(0x53); // S
    public static HotkeyGestureSettings DefaultClearCurrentWindowState => CreateDefault(0x52); // R

    public GlobalHotkeySettings Normalize() => this with
    {
        ToggleEnabled = NormalizeGesture(ToggleEnabled),
        ToggleMarkerVisibility = NormalizeGesture(ToggleMarkerVisibility),
        OpenSettings = NormalizeGesture(OpenSettings),
        ClearCurrentWindowState = NormalizeGesture(ClearCurrentWindowState)
    };

    private static HotkeyGestureSettings CreateDefault(uint virtualKey) => new()
    {
        Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
        VirtualKey = virtualKey
    };

    private static HotkeyGestureSettings? NormalizeGesture(HotkeyGestureSettings? gesture) =>
        gesture is { IsValid: true } ? gesture : null;
}

public sealed record MarkerAppearanceSettings
{
    public const string DefaultChineseColor = "#EF4444";
    public const string DefaultEnglishColor = "#2563EB";
    public const string DefaultCapsLockColor = "#F59E0B";
    private const string OldDefaultChineseColor = "#20BE62";
    private const string OldDefaultEnglishColor = "#808080";

    public MarkerStyle Style { get; init; } = MarkerStyle.Text;
    public int Size { get; init; } = 12;
    public int OffsetX { get; init; } = 6;
    public int OffsetY { get; init; } = 6;
    public string ChineseColor { get; init; } = DefaultChineseColor;
    public string EnglishColor { get; init; } = DefaultEnglishColor;
    public string CapsLockColor { get; init; } = DefaultCapsLockColor;
    public string ChineseText { get; init; } = "中";
    public string EnglishText { get; init; } = "英";
    public string CapsLockText { get; init; } = "A";
    public string? ChineseImagePath { get; init; }
    public string? EnglishImagePath { get; init; }
    public string? CapsLockImagePath { get; init; }

    public MarkerAppearanceSettings Normalize() => this with
    {
        Size = Math.Clamp(Size, 6, 96),
        OffsetX = Math.Clamp(OffsetX, -96, 96),
        OffsetY = Math.Clamp(OffsetY, -96, 96),
        ChineseColor = NormalizeColor(ChineseColor, DefaultChineseColor),
        EnglishColor = NormalizeColor(EnglishColor, DefaultEnglishColor),
        CapsLockColor = NormalizeColor(CapsLockColor, DefaultCapsLockColor),
        ChineseText = string.IsNullOrWhiteSpace(ChineseText) ? "中" : ChineseText.Trim(),
        EnglishText = string.IsNullOrWhiteSpace(EnglishText) ? "英" : EnglishText.Trim(),
        CapsLockText = string.IsNullOrWhiteSpace(CapsLockText) ? "A" : CapsLockText.Trim()
    };

    public MarkerAppearanceSettings MigrateOldDefaultColors() => this with
    {
        ChineseColor = NormalizeColor(ChineseColor, DefaultChineseColor) == OldDefaultChineseColor ? DefaultChineseColor : ChineseColor,
        EnglishColor = NormalizeColor(EnglishColor, DefaultEnglishColor) == OldDefaultEnglishColor ? DefaultEnglishColor : EnglishColor,
        Size = Size == 10 ? 12 : Size
    };

    private static string NormalizeColor(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        string trimmed = value.Trim();
        if (!trimmed.StartsWith('#'))
        {
            trimmed = "#" + trimmed;
        }

        return trimmed.Length == 7 && trimmed.Skip(1).All(Uri.IsHexDigit) ? trimmed.ToUpperInvariant() : fallback;
    }
}

public sealed record AppSettings
{
    public int SettingsVersion { get; init; } = 17;
    public bool Enabled { get; init; } = true;
    public bool StartWithWindows { get; init; } = false;
    public bool SilentStart { get; init; } = true;
    public bool AutoCheckForUpdates { get; init; } = true;
    public string StorageDirectory { get; init; } = string.Empty;
    public DiagnosticsLogLevel LogLevel { get; init; } = DiagnosticsLogLevel.Warn;
    public bool EnableWindowMemory { get; init; } = true;
    public bool PersistWindowMemory { get; init; } = false;
    public string WindowMemoryStoragePath { get; init; } = string.Empty;
    public SettingsWindowBackdrop SettingsBackdrop { get; init; } = SettingsWindowBackdrop.Acrylic;
    public MarkerAppearanceSettings Marker { get; init; } = new();
    public MarkerBehaviorSettings MarkerBehavior { get; init; } = new();
    public AdditionalIndicatorSettings AdditionalIndicators { get; init; } = new();
    public CaretCaptureMode CaretCaptureMode { get; init; } = CaretCaptureMode.Automatic;
    public bool GlobalHotkeysEnabled { get; init; } = true;
    public GlobalHotkeySettings Hotkeys { get; init; } = new();
    public IReadOnlyList<ApplicationRule> ApplicationRules { get; init; } = [];
    public IReadOnlyList<ImeDetectionRule> ImeDetectionRules { get; init; } = [];

    public AppSettings Normalize()
    {
        MarkerAppearanceSettings marker = (Marker ?? new MarkerAppearanceSettings()).MigrateOldDefaultColors();
        GlobalHotkeySettings hotkeys = (Hotkeys ?? new GlobalHotkeySettings()).Normalize();
        if (SettingsVersion < 9)
        {
            hotkeys = hotkeys with { Enabled = GlobalHotkeysEnabled };
        }

        string storageDirectory = ResolveStorageDirectoryForMigration();
        return this with
        {
            SettingsVersion = 17,
            StorageDirectory = storageDirectory,
            LogLevel = DiagnosticsLogLevelPolicy.Normalize(LogLevel),
            WindowMemoryStoragePath = string.Empty,
            SettingsBackdrop = Enum.IsDefined(SettingsBackdrop)
                ? SettingsBackdrop
                : SettingsWindowBackdrop.Acrylic,
            Marker = marker.Normalize(),
            MarkerBehavior = (MarkerBehavior ?? new MarkerBehaviorSettings()).Normalize(),
            AdditionalIndicators = (AdditionalIndicators ?? new AdditionalIndicatorSettings()).Normalize(),
            CaretCaptureMode = CaretCaptureModePolicy.Normalize(CaretCaptureMode),
            GlobalHotkeysEnabled = hotkeys.Enabled,
            Hotkeys = hotkeys,
            ApplicationRules = ApplicationRuleNormalizer.Normalize(ApplicationRules),
            ImeDetectionRules = ImeDetectionRuleNormalizer.Normalize(ImeDetectionRules)
        };
    }

    private string ResolveStorageDirectoryForMigration()
    {
        if (!string.IsNullOrWhiteSpace(StorageDirectory))
        {
            try
            {
                return StoragePathService.ResolveDirectory(StorageDirectory);
            }
            catch
            {
                return StoragePathService.DefaultDirectory;
            }
        }

        if (SettingsVersion < 17 && !string.IsNullOrWhiteSpace(WindowMemoryStoragePath))
        {
            try
            {
                string legacyPath = Path.GetFullPath(
                    Environment.ExpandEnvironmentVariables(WindowMemoryStoragePath.Trim()));
                string defaultLegacyPath = Path.GetFullPath(StoragePathService.LegacyWindowMemoryDefaultPath);
                if (!string.Equals(legacyPath, defaultLegacyPath, StringComparison.OrdinalIgnoreCase))
                {
                    string? legacyDirectory = Path.GetDirectoryName(legacyPath);
                    if (!string.IsNullOrWhiteSpace(legacyDirectory))
                    {
                        return StoragePathService.ResolveDirectory(legacyDirectory);
                    }
                }
            }
            catch
            {
            }
        }

        return StoragePathService.DefaultDirectory;
    }
}

public static class ApplicationRuleNormalizer
{
    public static IReadOnlyList<ApplicationRule> Normalize(IReadOnlyList<ApplicationRule>? rules)
    {
        if (rules is null || rules.Count == 0)
        {
            return [];
        }

        var combined = new Dictionary<string, ApplicationRule>(StringComparer.OrdinalIgnoreCase);
        foreach (ApplicationRule? rule in rules)
        {
            if (rule is null)
            {
                continue;
            }

            string processName = NormalizeProcessName(rule.ProcessName);
            string title = NormalizeContextValue(rule.WindowTitleContains);
            string windowClass = NormalizeContextValue(rule.WindowClass);
            string controlClass = NormalizeContextValue(rule.ControlClass);
            bool hideMarker = rule.HideMarker || rule.Excluded;
            bool disableWindowMemory = rule.DisableWindowMemory || rule.Excluded;
            bool disableStateRestore = rule.DisableStateRestore || rule.Excluded;
            if (string.IsNullOrEmpty(processName) ||
                (!hideMarker && !disableWindowMemory && !disableStateRestore &&
                 rule.OffsetX is null && rule.OffsetY is null))
            {
                continue;
            }

            string key = string.Join('\u001F', processName, title, windowClass, controlClass);
            var normalized = rule with
            {
                ProcessName = processName,
                WindowTitleContains = title,
                WindowClass = windowClass,
                ControlClass = controlClass,
                Excluded = false,
                HideMarker = hideMarker,
                DisableWindowMemory = disableWindowMemory,
                DisableStateRestore = disableStateRestore
            };

            if (combined.TryGetValue(key, out ApplicationRule? existing))
            {
                combined[key] = existing with
                {
                    HideMarker = existing.HideMarker || normalized.HideMarker,
                    DisableWindowMemory = existing.DisableWindowMemory || normalized.DisableWindowMemory,
                    DisableStateRestore = existing.DisableStateRestore || normalized.DisableStateRestore,
                    OffsetX = normalized.OffsetX ?? existing.OffsetX,
                    OffsetY = normalized.OffsetY ?? existing.OffsetY
                };
            }
            else
            {
                combined[key] = normalized;
            }
        }

        return combined.Values
            .OrderBy(rule => rule.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(rule => rule.WindowTitleContains, StringComparer.OrdinalIgnoreCase)
            .ThenBy(rule => rule.WindowClass, StringComparer.OrdinalIgnoreCase)
            .ThenBy(rule => rule.ControlClass, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        string name = Path.GetFileName(processName.Trim());
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
    }

    public static string NormalizeContextValue(string? value) => value?.Trim() ?? string.Empty;
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;

    public SettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ImeTool",
            "settings.json");
    }

    public string SettingsPath => _settingsPath;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings().Normalize();
            }

            string json = File.ReadAllText(_settingsPath);
            AppSettings source = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            AppSettings normalized = source.Normalize();
            TryMigrateLegacyWindowMemory(source, normalized);
            return normalized;
        }
        catch
        {
            return new AppSettings().Normalize();
        }
    }

    public void Save(AppSettings settings)
    {
        string? directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings.Normalize(), JsonOptions));
    }

    private static void TryMigrateLegacyWindowMemory(AppSettings source, AppSettings normalized)
    {
        if (source.SettingsVersion >= 17 || !source.PersistWindowMemory)
        {
            return;
        }

        try
        {
            string legacyPath = string.IsNullOrWhiteSpace(source.WindowMemoryStoragePath)
                ? StoragePathService.LegacyWindowMemoryDefaultPath
                : Path.GetFullPath(Environment.ExpandEnvironmentVariables(source.WindowMemoryStoragePath.Trim()));
            string destinationPath = StoragePathService.GetWindowMemoryPath(normalized.StorageDirectory);
            if (string.Equals(legacyPath, destinationPath, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(legacyPath) ||
                File.Exists(destinationPath))
            {
                return;
            }

            string? destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(legacyPath, destinationPath);
        }
        catch
        {
            // Startup falls back to an empty persisted-memory set if migration
            // is blocked by the selected storage directory.
        }
    }
}

