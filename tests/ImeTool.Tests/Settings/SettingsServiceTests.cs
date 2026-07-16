using ImeTool.Caret;
using ImeTool.Diagnostics;
using ImeTool.Ime;
using ImeTool.Settings;

namespace ImeTool.Tests.Settings;

public sealed class SettingsServiceTests
{
    [Fact]
    public void Load_Returns_Defaults_When_File_Does_Not_Exist()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        var service = new SettingsService(path);

        AppSettings settings = service.Load();

        Assert.True(settings.Enabled);
        Assert.False(settings.StartWithWindows);
        Assert.True(settings.SilentStart);
        Assert.True(settings.AutoCheckForUpdates);
        Assert.True(settings.EnableWindowMemory);
        Assert.False(settings.PersistWindowMemory);
        Assert.Equal(string.Empty, settings.WindowMemoryStoragePath);
        Assert.Equal(StoragePathService.DefaultDirectory, settings.StorageDirectory, ignoreCase: true);
        Assert.Equal(DiagnosticsLogLevel.Warn, settings.LogLevel);
        Assert.Equal(SettingsWindowBackdrop.Acrylic, settings.SettingsBackdrop);
        Assert.Equal(MarkerDisplayMode.Always, settings.MarkerBehavior.DisplayMode);
        Assert.Equal(CaretCaptureMode.Automatic, settings.CaretCaptureMode);
        Assert.True(settings.GlobalHotkeysEnabled);
    }

    [Fact]
    public void Save_Then_Load_RoundTrips_Settings()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        var service = new SettingsService(path);
        var expected = new AppSettings
        {
            Enabled = false,
            StartWithWindows = true,
            SilentStart = false,
            AutoCheckForUpdates = false,
            CaretCaptureMode = CaretCaptureMode.UiAutomation
        };

        service.Save(expected);
        AppSettings actual = service.Load();

        Assert.False(actual.Enabled);
        Assert.True(actual.StartWithWindows);
        Assert.False(actual.SilentStart);
        Assert.False(actual.AutoCheckForUpdates);
        Assert.Equal(CaretCaptureMode.UiAutomation, actual.CaretCaptureMode);
    }

    [Theory]
    [InlineData(CaretCaptureMode.Automatic)]
    [InlineData(CaretCaptureMode.Win32)]
    [InlineData(CaretCaptureMode.UiAutomation)]
    [InlineData(CaretCaptureMode.Msaa)]
    [InlineData(CaretCaptureMode.BrowserCompatibility)]
    [InlineData(CaretCaptureMode.JavaAccessBridge)]
    public void Save_Then_Load_RoundTrips_All_Caret_Capture_Modes(CaretCaptureMode mode)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        var service = new SettingsService(path);

        service.Save(new AppSettings { CaretCaptureMode = mode });

        Assert.Equal(mode, service.Load().CaretCaptureMode);
    }

    [Fact]
    public void Normalize_Resets_Invalid_Caret_Capture_Mode()
    {
        AppSettings settings = new AppSettings
        {
            CaretCaptureMode = (CaretCaptureMode)99
        }.Normalize();

        Assert.Equal(CaretCaptureMode.Automatic, settings.CaretCaptureMode);
    }

    [Fact]
    public void Load_Returns_Defaults_For_Corrupt_Json()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, "{ not valid json");
        var service = new SettingsService(path);

        AppSettings settings = service.Load();

        Assert.True(settings.Enabled);
        Assert.False(settings.StartWithWindows);
    }

    [Fact]
    public void Save_Then_Load_RoundTrips_Settings_Window_Backdrop()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        var service = new SettingsService(path);
        var expected = new AppSettings
        {
            SettingsBackdrop = SettingsWindowBackdrop.Acrylic
        };

        service.Save(expected);
        AppSettings actual = service.Load();

        Assert.Equal(SettingsWindowBackdrop.Acrylic, actual.SettingsBackdrop);
    }

    [Fact]
    public void Save_Then_Load_RoundTrips_Second_Priority_Settings()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        var service = new SettingsService(path);
        var expected = new AppSettings
        {
            GlobalHotkeysEnabled = false,
            Hotkeys = new GlobalHotkeySettings { Enabled = false },
            MarkerBehavior = new MarkerBehaviorSettings
            {
                DisplayMode = MarkerDisplayMode.WhileTyping,
                AutoHideDelayMilliseconds = 2200,
                EnableMotion = false,
                FollowAnimationDurationMilliseconds = 180,
                EnableFadeAnimation = false
            },
            ApplicationRules =
            [
                new ApplicationRule { ProcessName = "Chrome.exe", Excluded = true },
                new ApplicationRule { ProcessName = "Code", DisableStateRestore = true }
            ],
            ImeDetectionRules =
            [
                new ImeDetectionRule
                {
                    KeyboardLayout = "0x8040804",
                    OpenStatusCode = 1,
                    ConversionMode = 1025,
                    Result = TextInputMode.Chinese
                }
            ]
        };

        service.Save(expected);
        AppSettings actual = service.Load();

        Assert.False(actual.GlobalHotkeysEnabled);
        Assert.False(actual.Hotkeys.Enabled);
        Assert.Equal(MarkerDisplayMode.WhileTyping, actual.MarkerBehavior.DisplayMode);
        Assert.Equal(2200, actual.MarkerBehavior.AutoHideDelayMilliseconds);
        Assert.False(actual.MarkerBehavior.EnableMotion);
        Assert.Equal(180, actual.MarkerBehavior.FollowAnimationDurationMilliseconds);
        Assert.False(actual.MarkerBehavior.EnableFadeAnimation);
        Assert.Equal(2, actual.ApplicationRules.Count);
        Assert.Contains(actual.ApplicationRules, rule =>
            rule.ProcessName == "Chrome" && rule.HideMarker &&
            rule.DisableWindowMemory && rule.DisableStateRestore);
        Assert.Contains(actual.ApplicationRules, rule => rule.ProcessName == "Code" && rule.DisableStateRestore);
        ImeDetectionRule detectionRule = Assert.Single(actual.ImeDetectionRules);
        Assert.Equal("0x0000000008040804", detectionRule.KeyboardLayout);
        Assert.Equal(TextInputMode.Chinese, detectionRule.Result);
        Assert.Equal(17, actual.SettingsVersion);
    }

    [Fact]
    public void Normalize_Clamps_Behavior_Values_And_Drops_Empty_Rules()
    {
        AppSettings settings = new AppSettings
        {
            MarkerBehavior = new MarkerBehaviorSettings
            {
                AutoHideDelayMilliseconds = 1,
                FollowAnimationDurationMilliseconds = 999
            },
            ApplicationRules =
            [
                new ApplicationRule { ProcessName = " ", Excluded = true },
                new ApplicationRule { ProcessName = "notepad.exe" },
                new ApplicationRule { ProcessName = "valid.exe", Excluded = true }
            ]
        }.Normalize();

        Assert.Equal(300, settings.MarkerBehavior.AutoHideDelayMilliseconds);
        Assert.Equal(300, settings.MarkerBehavior.FollowAnimationDurationMilliseconds);
        Assert.Single(settings.ApplicationRules);
        Assert.Equal("valid", settings.ApplicationRules[0].ProcessName);
    }

    [Fact]
    public void Save_Then_Load_RoundTrips_Advanced_Application_Rule()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        var service = new SettingsService(path);

        service.Save(new AppSettings
        {
            ApplicationRules =
            [
                new ApplicationRule
                {
                    ProcessName = "Telegram.exe",
                    WindowTitleContains = "Chat",
                    WindowClass = "QtWindow",
                    ControlClass = "InputControl",
                    HideMarker = true,
                    DisableWindowMemory = false,
                    DisableStateRestore = true,
                    OffsetX = 12,
                    OffsetY = -4
                }
            ]
        });

        ApplicationRule rule = Assert.Single(service.Load().ApplicationRules);
        Assert.Equal("Telegram", rule.ProcessName);
        Assert.Equal("Chat", rule.WindowTitleContains);
        Assert.Equal("QtWindow", rule.WindowClass);
        Assert.Equal("InputControl", rule.ControlClass);
        Assert.True(rule.HideMarker);
        Assert.False(rule.DisableWindowMemory);
        Assert.True(rule.DisableStateRestore);
        Assert.Equal(12, rule.OffsetX);
        Assert.Equal(-4, rule.OffsetY);
    }

    [Fact]
    public void Save_Then_Load_RoundTrips_Custom_And_Removed_Hotkeys()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        var service = new SettingsService(path);
        var custom = new HotkeyGestureSettings
        {
            Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift,
            VirtualKey = 0x4B
        };

        service.Save(new AppSettings
        {
            Hotkeys = new GlobalHotkeySettings
            {
                ToggleEnabled = custom,
                ToggleMarkerVisibility = null,
                OpenSettings = GlobalHotkeySettings.DefaultOpenSettings,
                ClearCurrentWindowState = null
            }
        });

        AppSettings actual = service.Load();

        Assert.Equal(custom, actual.Hotkeys.ToggleEnabled);
        Assert.Null(actual.Hotkeys.ToggleMarkerVisibility);
        Assert.Equal(GlobalHotkeySettings.DefaultOpenSettings, actual.Hotkeys.OpenSettings);
        Assert.Null(actual.Hotkeys.ClearCurrentWindowState);
    }

    [Fact]
    public void Version_Eight_Global_Hotkey_Flag_Migrates_To_New_Model()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, """
            {
              "SettingsVersion": 8,
              "GlobalHotkeysEnabled": false
            }
            """);
        var service = new SettingsService(path);

        AppSettings actual = service.Load();

        Assert.False(actual.GlobalHotkeysEnabled);
        Assert.False(actual.Hotkeys.Enabled);
        Assert.Equal(GlobalHotkeySettings.DefaultToggleEnabled, actual.Hotkeys.ToggleEnabled);
    }

    [Fact]
    public void Version_Nine_Settings_Enable_Automatic_Update_Checks_By_Default()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, """
            {
              "SettingsVersion": 9,
              "Enabled": true
            }
            """);
        var service = new SettingsService(path);

        AppSettings actual = service.Load();

        Assert.True(actual.AutoCheckForUpdates);
        Assert.Equal(17, actual.SettingsVersion);

    }

    [Fact]
    public void Save_Then_Load_RoundTrips_Storage_And_Window_Memory_Setting()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        var service = new SettingsService(path);

        service.Save(new AppSettings
        {
            EnableWindowMemory = false,
            PersistWindowMemory = true,
            StorageDirectory = "D:\\ImeToolData",
            LogLevel = DiagnosticsLogLevel.Info
        });
        AppSettings actual = service.Load();

        Assert.False(actual.EnableWindowMemory);
        Assert.True(actual.PersistWindowMemory);
        Assert.Equal(Path.GetFullPath("D:\\ImeToolData"), actual.StorageDirectory, ignoreCase: true);
        Assert.Equal(DiagnosticsLogLevel.Info, actual.LogLevel);
        Assert.Equal(string.Empty, actual.WindowMemoryStoragePath);
    }

    [Fact]
    public void Version_Sixteen_Custom_Window_Memory_Path_Migrates_To_Storage_Directory()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        string legacyDirectory = Path.Combine(directory, "legacy-data");
        string legacyMemoryPath = Path.Combine(legacyDirectory, "custom-memory.json");
        File.WriteAllText(path, $$"""
            {
              "SettingsVersion": 16,
              "WindowMemoryStoragePath": "{{legacyMemoryPath.Replace("\\", "\\\\")}}"
            }
            """);

        AppSettings actual = new SettingsService(path).Load();

        Assert.Equal(17, actual.SettingsVersion);
        Assert.Equal(Path.GetFullPath(legacyDirectory), actual.StorageDirectory, ignoreCase: true);
        Assert.Equal(string.Empty, actual.WindowMemoryStoragePath);
    }

    [Fact]
    public void Version_Sixteen_Persisted_Window_Memory_File_Is_Copied_To_Unified_Name()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string legacyDirectory = Path.Combine(directory, "legacy-data");
        Directory.CreateDirectory(legacyDirectory);
        string legacyMemoryPath = Path.Combine(legacyDirectory, "custom-memory.json");
        File.WriteAllText(legacyMemoryPath, "{\"Version\":1,\"Entries\":[]}");
        string settingsPath = Path.Combine(directory, "settings.json");
        File.WriteAllText(settingsPath, $$"""
            {
              "SettingsVersion": 16,
              "PersistWindowMemory": true,
              "WindowMemoryStoragePath": "{{legacyMemoryPath.Replace("\\", "\\\\")}}"
            }
            """);

        AppSettings actual = new SettingsService(settingsPath).Load();
        string unifiedPath = StoragePathService.GetWindowMemoryPath(actual.StorageDirectory);

        Assert.True(File.Exists(unifiedPath));
        Assert.Equal(File.ReadAllText(legacyMemoryPath), File.ReadAllText(unifiedPath));
    }

    [Fact]
    public void Invalid_Hotkey_Gesture_Is_Normalized_To_Removed()
    {
        GlobalHotkeySettings settings = new GlobalHotkeySettings
        {
            ToggleEnabled = new HotkeyGestureSettings
            {
                Modifiers = HotkeyModifiers.None,
                VirtualKey = 0x41
            }
        }.Normalize();

        Assert.Null(settings.ToggleEnabled);
    }
}
