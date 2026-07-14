using System.IO;
using System.Text.Json;

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

public sealed record ApplicationRule
{
    public string ProcessName { get; init; } = string.Empty;
    public bool Excluded { get; init; }
    public bool DisableStateRestore { get; init; }
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
    public int SettingsVersion { get; init; } = 9;
    public bool Enabled { get; init; } = true;
    public bool StartWithWindows { get; init; } = false;
    public bool SilentStart { get; init; } = true;
    public SettingsWindowBackdrop SettingsBackdrop { get; init; } = SettingsWindowBackdrop.Acrylic;
    public MarkerAppearanceSettings Marker { get; init; } = new();
    public MarkerBehaviorSettings MarkerBehavior { get; init; } = new();
    public bool GlobalHotkeysEnabled { get; init; } = true;
    public GlobalHotkeySettings Hotkeys { get; init; } = new();
    public IReadOnlyList<ApplicationRule> ApplicationRules { get; init; } = [];

    public AppSettings Normalize()
    {
        MarkerAppearanceSettings marker = (Marker ?? new MarkerAppearanceSettings()).MigrateOldDefaultColors();
        GlobalHotkeySettings hotkeys = (Hotkeys ?? new GlobalHotkeySettings()).Normalize();
        if (SettingsVersion < 9)
        {
            hotkeys = hotkeys with { Enabled = GlobalHotkeysEnabled };
        }

        return this with
        {
            SettingsVersion = 9,
            SettingsBackdrop = Enum.IsDefined(SettingsBackdrop)
                ? SettingsBackdrop
                : SettingsWindowBackdrop.Acrylic,
            Marker = marker.Normalize(),
            MarkerBehavior = (MarkerBehavior ?? new MarkerBehaviorSettings()).Normalize(),
            GlobalHotkeysEnabled = hotkeys.Enabled,
            Hotkeys = hotkeys,
            ApplicationRules = ApplicationRuleNormalizer.Normalize(ApplicationRules)
        };
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
            if (string.IsNullOrEmpty(processName) || (!rule.Excluded && !rule.DisableStateRestore))
            {
                continue;
            }

            if (combined.TryGetValue(processName, out ApplicationRule? existing))
            {
                combined[processName] = existing with
                {
                    Excluded = existing.Excluded || rule.Excluded,
                    DisableStateRestore = existing.DisableStateRestore || rule.DisableStateRestore
                };
            }
            else
            {
                combined[processName] = rule with { ProcessName = processName };
            }
        }

        return combined.Values
            .OrderBy(rule => rule.ProcessName, StringComparer.OrdinalIgnoreCase)
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
                return new AppSettings();
            }

            string json = File.ReadAllText(_settingsPath);
            return (JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings()).Normalize();
        }
        catch
        {
            return new AppSettings();
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
}

