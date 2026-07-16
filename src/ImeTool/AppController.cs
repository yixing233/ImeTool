using System.Windows.Threading;
using System.Text;
using ImeTool.Caret;
using ImeTool.Diagnostics;
using ImeTool.Ime;
using ImeTool.Hotkeys;
using ImeTool.Native;
using ImeTool.Overlay;
using ImeTool.Settings;
using ImeTool.State;
using ImeTool.Tracking;
using ImeTool.Tray;
using ImeTool.Updates;

namespace ImeTool;

public sealed class AppController : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly StartupManager _startupManager;
    private readonly ICaretService _caretService;
    private readonly CaretSnapshotStabilizer _caretStabilizer;
    private readonly MarkerVisibilityController _markerVisibility;
    private readonly ICapsLockService _capsLockService;
    private readonly InferredInputModeTracker _inferredInputModeTracker;
    private readonly IWindowInfoService _windowInfoService;
    private readonly IImeService _imeService;
    private readonly WindowStateStore _stateStore;
    private readonly WindowMemoryManager _windowMemory;
    private readonly FocusTracker _focusTracker;
    private readonly MarkerOverlay _overlay;
    private readonly WinEventHook _eventHook;
    private readonly TrayIcon _trayIcon;
    private readonly GlobalHotkeyService _globalHotkeys;
    private readonly GitHubUpdateService _updateService;
    private readonly CancellationTokenSource _updateCancellation = new();
    private readonly ProcessNameResolver _processNameResolver;
    private readonly DispatcherTimer _timer;
    private AppSettings _settings;
    private ApplicationRuleMatcher _applicationRuleMatcher;
    private bool _markerTemporarilyHidden;
    private bool _settingsWindowOpen;
    private bool _disposed;

    public AppController()
    {
        DiagnosticsLog.Write("ImeTool starting.");
        _settingsService = new SettingsService();
        _startupManager = new StartupManager();
        _settings = _settingsService.Load();
        bool registryStartupEnabled = _startupManager.IsEnabled();
        if (_settings.StartWithWindows != registryStartupEnabled)
        {
            _settings = _settings with { StartWithWindows = registryStartupEnabled };
            _settingsService.Save(_settings);
        }

        _caretService = new CaretService();
        _caretStabilizer = new CaretSnapshotStabilizer();
        _markerVisibility = new MarkerVisibilityController();
        _capsLockService = new CapsLockService();
        _inferredInputModeTracker = new InferredInputModeTracker();
        _windowInfoService = new WindowInfoService();
        _imeService = new ImeService();
        _stateStore = new WindowStateStore();
        _windowMemory = new WindowMemoryManager(_stateStore, _settings.EnableWindowMemory);
        ConfigureWindowMemoryPersistence(_settings);
        _processNameResolver = new ProcessNameResolver();
        _applicationRuleMatcher = new ApplicationRuleMatcher(_settings.ApplicationRules);
        _focusTracker = new FocusTracker(
            _imeService,
            _stateStore,
            _windowInfoService,
            CanTrackWindowState);
        _overlay = new MarkerOverlay();
        _overlay.ConfigureBehavior(_settings.MarkerBehavior);
        _eventHook = new WinEventHook();
        _trayIcon = new TrayIcon(_settings);
        _globalHotkeys = new GlobalHotkeyService();
        _updateService = new GitHubUpdateService();
        _trayIcon.SetEnabledChecked(_settings.Enabled);
        _trayIcon.SetStartupChecked(_settings.StartWithWindows);

        _eventHook.FocusChanged += OnFocusChanged;
        _focusTracker.FallbackInputModeApplied += OnFallbackInputModeApplied;
        _trayIcon.ToggleEnabledRequested += OnToggleEnabledRequested;
        _trayIcon.ToggleStartupRequested += OnToggleStartupRequested;
        _trayIcon.SettingsRequested += OnSettingsRequested;
        _trayIcon.ExitRequested += OnExitRequested;
        _globalHotkeys.CommandInvoked += OnGlobalHotkeyCommand;
        _globalHotkeys.SetSettings(_settings.Hotkeys);

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _timer.Tick += OnTimerTick;
    }

    public void Start()
    {
        _timer.Start();
        IntPtr foregroundWindow = NativeMethods.GetForegroundWindow();
        if (foregroundWindow != IntPtr.Zero)
        {
            OnFocusChanged(this, foregroundWindow);
        }

        if (_settings.AutoCheckForUpdates)
        {
            _ = CheckForUpdatesAfterStartupAsync(_updateCancellation.Token);
        }
    }

    private async Task CheckForUpdatesAfterStartupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
            UpdateCheckResult result = await _updateService.CheckForUpdatesAsync(cancellationToken: cancellationToken);
            if (result.Availability == UpdateAvailability.Available && result.Release is not null)
            {
                DiagnosticsLog.Write($"Update available: {result.Release.TagName}.");
                _trayIcon.ShowUpdateAvailable(result.Release.Version);
            }
            else
            {
                DiagnosticsLog.Write($"Update check completed: {result.Availability}.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            DiagnosticsLog.Write($"Automatic update check failed: {exception.Message}");
        }
    }

    public bool SilentStart => _settings.SilentStart;

    public void ShowSettings()
    {
        OnSettingsRequested(this, EventArgs.Empty);
    }

    public void ShowTrayMenu()
    {
        DiagnosticsLog.Write("Showing development tray menu preview.");
        _trayIcon.ShowMenuForDevelopment();
    }

    private void OnFocusChanged(object? sender, IntPtr hwnd)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _caretService.Invalidate();
            _caretStabilizer.Reset();
            _markerVisibility.Reset();
            _overlay.HideMarker(immediate: true);
            if (_settings.Enabled)
            {
                _focusTracker.HandleFocusChanged(hwnd);
            }
        });
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _overlay.ConfigureBehavior(_settings.MarkerBehavior);
        if (!_settings.Enabled)
        {
            DiagnosticsLog.WriteThrottled("Marker hidden: app disabled.");
            _markerVisibility.Reset();
            _overlay.HideMarker();
            return;
        }

        _windowMemory.Prune(IsWindowKeyAlive);
        _inferredInputModeTracker.Prune(_windowInfoService.IsWindow);

        if (!_caretService.TryGetCaret(out CaretSnapshot caret))
        {
            _caretStabilizer.Reset();
            _markerVisibility.Reset();
            string reason = _caretService.LastFailureReason ?? "no caret from GetGUIThreadInfo or UI Automation";
            DiagnosticsLog.WriteThrottled($"Marker hidden: {reason}");
            _overlay.HideMarker();
            return;
        }

        caret = _caretStabilizer.Stabilize(caret);

        NativeMethods.GetWindowThreadProcessId(caret.FocusHwnd, out uint focusProcessId);
        if (focusProcessId == Environment.ProcessId)
        {
            DiagnosticsLog.WriteThrottled("Marker hidden: focus belongs to ImeTool.");
            _markerVisibility.Reset();
            _overlay.HideMarker();
            return;
        }

        bool hasWindowKey = _windowInfoService.TryGetWindowKey(caret.FocusHwnd, out WindowKey key);
        if (hasWindowKey)
        {
            bool isVisible = _windowInfoService.IsWindowVisible(key.Hwnd);
            bool isMinimized = _windowInfoService.IsIconic(key.Hwnd);
            if (!isVisible || isMinimized)
            {
                DiagnosticsLog.WriteThrottled($"Marker hidden: window not visible or minimized {key}.");
                _markerVisibility.Reset();
                _overlay.HideMarker();
                return;
            }

            ApplicationRuleMatch rule = GetApplicationRule(key.ProcessId);
            if (rule.Excluded)
            {
                DiagnosticsLog.WriteThrottled($"Marker hidden: application excluded by rule for {key}.");
                _markerVisibility.Reset();
                _overlay.HideMarker();
                return;
            }

            if (WindowMemoryObservationPolicy.ShouldObserve(
                    hasValidatedTextCaret: true,
                    hasWindowKey: true,
                    isOwnProcess: false,
                    isVisible: isVisible,
                    isMinimized: isMinimized,
                    isExcluded: rule.Excluded))
            {
                WindowMemoryObservationResult observation = ObserveWindow(caret.FocusHwnd);
                if (observation.HasPersistedState)
                {
                    _focusTracker.RequestRestoreCurrentWindowState(
                        caret.FocusHwnd,
                        validatedInputTarget: true);
                }
            }
        }
        ImeOpenStatus status = _focusTracker.UpdateCurrentImeState(caret.FocusHwnd);
        if (status == ImeOpenStatus.Unknown)
        {
            DiagnosticsLog.WriteThrottled(
                $"IME live status unknown for hwnd 0x{caret.FocusHwnd.ToInt64():X}; window memory is not used for marker display.");
        }

        TextInputMode reportedInputMode = _imeService.GetInputMode(caret.FocusHwnd);
        TextInputMode inputMode = reportedInputMode;
        if (hasWindowKey)
        {
            inputMode = _inferredInputModeTracker.Resolve(key, reportedInputMode);
            bool canRememberInputMode = reportedInputMode != TextInputMode.Unknown ||
                                        _inferredInputModeTracker.HasEffectiveOverride(key);
            if (canRememberInputMode)
            {
                bool verifiedInputMode = reportedInputMode != TextInputMode.Unknown ||
                                         _inferredInputModeTracker.HasEffectiveOverride(key);
                _focusTracker.RecordCurrentInputMode(key, inputMode, verifiedInputMode);
                if (CanTrackWindowState(key))
                {
                    _windowMemory.UpdateStatus(
                        key,
                        inputMode == TextInputMode.Chinese
                            ? ImeOpenStatus.Open
                            : ImeOpenStatus.Closed);
                }
            }

        }

        if (inputMode == TextInputMode.Unknown)
        {
            inputMode = status switch
            {
                ImeOpenStatus.Open => TextInputMode.Chinese,
                ImeOpenStatus.Closed => TextInputMode.English,
                _ => TextInputMode.Unknown
            };
        }

        MarkerState markerState = MarkerStateResolver.Resolve(inputMode, _capsLockService.IsCapsLockOn());
        bool strategyAllowsDisplay = _markerVisibility.ShouldShow(
            DateTimeOffset.UtcNow,
            _settings.MarkerBehavior,
            caret.FocusHwnd,
            markerState,
            caret.ScreenRect);
        if (_markerTemporarilyHidden || !strategyAllowsDisplay)
        {
            string reason = _markerTemporarilyHidden ? "temporarily hidden" : "display strategy idle";
            DiagnosticsLog.WriteThrottled($"Marker hidden: {reason}.");
            _overlay.HideMarker();
            return;
        }

        _overlay.Update(markerState, caret.ScreenRect, _settings.Marker, _settings.MarkerBehavior);
        DiagnosticsLog.WriteThrottled(
            $"Marker shown: state={markerState}, inputMode={inputMode}, imeStatus={status}, source={caret.Source}, hwnd=0x{caret.FocusHwnd.ToInt64():X}, caret=({caret.ScreenRect.Left},{caret.ScreenRect.Top},{caret.ScreenRect.Right},{caret.ScreenRect.Bottom}), style={_settings.Marker.Style}.",
            $"marker-shown:{markerState}:{caret.Source}:{caret.FocusHwnd}");
    }

    private void OnToggleEnabledRequested(object? sender, EventArgs e)
    {
        _settings = _settings with { Enabled = !_settings.Enabled };
        _settingsService.Save(_settings);
        _trayIcon.SetEnabledChecked(_settings.Enabled);
        DiagnosticsLog.Write($"Enabled changed: {_settings.Enabled}.");
        if (!_settings.Enabled)
        {
            _markerVisibility.Reset();
            _overlay.HideMarker();
        }
    }

    private void OnToggleStartupRequested(object? sender, EventArgs e)
    {
        bool newValue = !_settings.StartWithWindows;
        _startupManager.SetEnabled(newValue);
        _settings = _settings with { StartWithWindows = newValue };
        _settingsService.Save(_settings);
        _trayIcon.SetStartupChecked(newValue);
        DiagnosticsLog.Write($"StartWithWindows changed: {newValue}.");
    }

    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        if (_settingsWindowOpen)
        {
            return;
        }

        _settingsWindowOpen = true;
        var window = new SettingsWindow(_settings, _windowMemory)
        {
            Topmost = false
        };

        bool? result;
        _globalHotkeys.SetSettings(_settings.Hotkeys with { Enabled = false });
        try
        {
            result = window.ShowDialog();
        }
        finally
        {
            _settingsWindowOpen = false;
            _globalHotkeys.SetSettings(_settings.Hotkeys);
        }

        if (result != true)
        {
            return;
        }

        AppSettings newSettings = window.Settings.Normalize();
        if (newSettings.StartWithWindows != _settings.StartWithWindows)
        {
            _startupManager.SetEnabled(newSettings.StartWithWindows);
        }

        _settings = newSettings;
        ConfigureWindowMemoryPersistence(_settings);
        _windowMemory.SetGlobalEnabled(_settings.EnableWindowMemory);
        if (_settings.EnableWindowMemory && _settings.PersistWindowMemory)
        {
            RequestRememberedStateForForegroundWindow();
        }
        _applicationRuleMatcher = new ApplicationRuleMatcher(_settings.ApplicationRules);
        _processNameResolver.Clear();
        _markerVisibility.Reset();
        _overlay.ConfigureBehavior(_settings.MarkerBehavior);
        _globalHotkeys.SetSettings(_settings.Hotkeys);
        _settingsService.Save(_settings);
        _trayIcon.SetEnabledChecked(_settings.Enabled);
        _trayIcon.SetStartupChecked(_settings.StartWithWindows);
        DiagnosticsLog.Write($"Settings saved: enabled={_settings.Enabled}, silentStart={_settings.SilentStart}, style={_settings.Marker.Style}, size={_settings.Marker.Size}, offset=({_settings.Marker.OffsetX},{_settings.Marker.OffsetY}).");
        if (!_settings.Enabled)
        {
            _overlay.HideMarker();
        }
    }

    private ApplicationRuleMatch GetApplicationRule(uint processId)
    {
        if (!_applicationRuleMatcher.HasRules)
        {
            return ApplicationRuleMatch.None;
        }

        return _applicationRuleMatcher.Match(_processNameResolver.Resolve(processId));
    }

    private bool CanTrackWindowState(WindowKey key)
    {
        ApplicationRuleMatch rule = GetApplicationRule(key.ProcessId);
        return _windowMemory.CanTrack(key) && !rule.Excluded && !rule.DisableStateRestore;
    }

    private WindowMemoryObservationResult ObserveWindow(IntPtr hwnd)
    {
        if (!_windowInfoService.TryGetWindowKey(hwnd, out WindowKey key) ||
            key.ProcessId == Environment.ProcessId)
        {
            return WindowMemoryObservationResult.None;
        }

        string processName = _processNameResolver.Resolve(key.ProcessId) ?? $"PID {key.ProcessId}";
        string title = ReadWindowTitle(key.Hwnd);
        return _windowMemory.ObserveWindow(key, title, processName, DateTimeOffset.Now);
    }

    private void ConfigureWindowMemoryPersistence(AppSettings settings)
    {
        if (!settings.PersistWindowMemory)
        {
            _windowMemory.ConfigurePersistence(enabled: false, persistenceStore: null);
            return;
        }

        try
        {
            _windowMemory.ConfigurePersistence(
                enabled: true,
                new WindowMemoryPersistenceStore(settings.WindowMemoryStoragePath));
        }
        catch (Exception exception)
        {
            DiagnosticsLog.Write($"Window memory persistence configuration failed: {exception.Message}");
            _windowMemory.ConfigurePersistence(enabled: false, persistenceStore: null);
        }
    }

    private void RequestRememberedStateForForegroundWindow()
    {
        IntPtr foregroundWindow = NativeMethods.GetForegroundWindow();
        if (foregroundWindow != IntPtr.Zero)
        {
            _focusTracker.RequestRestoreCurrentWindowState(foregroundWindow);
        }
    }

    private bool IsWindowKeyAlive(WindowKey key) =>
        _windowInfoService.TryGetWindowKey(key.Hwnd, out WindowKey current) && current == key;

    private static string ReadWindowTitle(IntPtr hwnd)
    {
        int length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var title = new StringBuilder(length + 1);
        return NativeMethods.GetWindowText(hwnd, title, title.Capacity) > 0
            ? title.ToString()
            : string.Empty;
    }

    private void OnGlobalHotkeyCommand(object? sender, GlobalHotkeyCommand command)
    {
        switch (command)
        {
            case GlobalHotkeyCommand.ToggleEnabled:
                OnToggleEnabledRequested(this, EventArgs.Empty);
                break;
            case GlobalHotkeyCommand.ToggleMarkerVisibility:
                _markerTemporarilyHidden = !_markerTemporarilyHidden;
                _markerVisibility.Reset();
                if (_markerTemporarilyHidden)
                {
                    _overlay.HideMarker();
                }

                DiagnosticsLog.Write($"Temporary marker visibility changed: hidden={_markerTemporarilyHidden}.");
                break;
            case GlobalHotkeyCommand.OpenSettings:
                OnSettingsRequested(this, EventArgs.Empty);
                break;
            case GlobalHotkeyCommand.ClearCurrentWindowState:
                if (_focusTracker.CurrentWindow is WindowKey currentWindow)
                {
                    bool removed = _stateStore.Remove(currentWindow);
                    DiagnosticsLog.Write($"Cleared current window IME state: window={currentWindow}, removed={removed}.");
                }

                break;
        }
    }

    private void OnFallbackInputModeApplied(WindowKey key, TextInputMode mode)
    {
        _inferredInputModeTracker.SetEffectiveMode(key, mode);
        DiagnosticsLog.Write($"Input mode inference aligned with automatic restore: window={key}, mode={mode}.");
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DiagnosticsLog.Write("ImeTool exiting.");
        _updateCancellation.Cancel();
        _timer.Stop();
        _eventHook.Dispose();
        _focusTracker.FallbackInputModeApplied -= OnFallbackInputModeApplied;
        _globalHotkeys.Dispose();
        _updateService.Dispose();
        _updateCancellation.Dispose();
        _trayIcon.Dispose();
        _overlay.HideMarker(immediate: true);
        _overlay.Close();
        if (_capsLockService is IDisposable disposableCapsLockService)
        {
            disposableCapsLockService.Dispose();
        }

        if (_imeService is IDisposable disposableImeService)
        {
            disposableImeService.Dispose();
        }

        if (_caretService is IDisposable disposableCaretService)
        {
            disposableCaretService.Dispose();
        }
    }
}
