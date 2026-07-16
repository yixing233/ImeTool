using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Input;
using ImeTool.Diagnostics;
using ImeTool.Native;
using ImeTool.Settings;

namespace ImeTool.Hotkeys;

public enum GlobalHotkeyCommand
{
    ToggleEnabled = 1,
    ToggleMarkerVisibility = 2,
    OpenSettings = 3,
    ClearCurrentWindowState = 4
}

public sealed class GlobalHotkeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly HashSet<int> _registeredIds = [];
    private GlobalHotkeySettings _settings = new() { Enabled = false };
    private bool _disposed;

    public GlobalHotkeyService()
    {
        var parameters = new HwndSourceParameters("ImeTool.GlobalHotkeys")
        {
            ParentWindow = NativeMethods.HwndMessage,
            WindowStyle = 0,
            ExtendedWindowStyle = NativeMethods.WsExNoActivate
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WindowMessageHook);
    }

    public event EventHandler<GlobalHotkeyCommand>? CommandInvoked;

    public bool Enabled => _settings.Enabled;

    public void SetSettings(GlobalHotkeySettings settings)
    {
        GlobalHotkeySettings normalized = settings.Normalize();
        if (_disposed || _settings == normalized)
        {
            return;
        }

        UnregisterBindings();
        _settings = normalized;
        if (_settings.Enabled)
        {
            RegisterBindings();
        }
    }

    private void RegisterBindings()
    {
        foreach ((GlobalHotkeyCommand command, HotkeyGestureSettings? gesture) in GetBindings(_settings))
        {
            if (gesture is not { IsValid: true })
            {
                continue;
            }

            int id = (int)command;
            uint modifiers = (uint)gesture.Modifiers | NativeMethods.ModNoRepeat;
            if (NativeMethods.RegisterHotKey(_source.Handle, id, modifiers, gesture.VirtualKey))
            {
                _registeredIds.Add(id);
            }
            else
            {
                DiagnosticsLog.Warn(
                    $"Global hotkey registration failed: command={command}, gesture={HotkeyGestureFormatter.Format(gesture)}, error={Marshal.GetLastWin32Error()}.");
            }
        }
    }

    private static IEnumerable<(GlobalHotkeyCommand Command, HotkeyGestureSettings? Gesture)> GetBindings(
        GlobalHotkeySettings settings)
    {
        yield return (GlobalHotkeyCommand.ToggleEnabled, settings.ToggleEnabled);
        yield return (GlobalHotkeyCommand.ToggleMarkerVisibility, settings.ToggleMarkerVisibility);
        yield return (GlobalHotkeyCommand.OpenSettings, settings.OpenSettings);
        yield return (GlobalHotkeyCommand.ClearCurrentWindowState, settings.ClearCurrentWindowState);
    }

    private void UnregisterBindings()
    {
        foreach (int id in _registeredIds)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        }

        _registeredIds.Clear();
    }

    private IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == NativeMethods.WmHotkey &&
            _settings.Enabled &&
            Enum.IsDefined(typeof(GlobalHotkeyCommand), wParam.ToInt32()))
        {
            handled = true;
            CommandInvoked?.Invoke(this, (GlobalHotkeyCommand)wParam.ToInt32());
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterBindings();
        _source.RemoveHook(WindowMessageHook);
        _source.Dispose();
    }
}

public static class HotkeyGestureFormatter
{
    public static string Format(HotkeyGestureSettings? gesture)
    {
        if (gesture is not { IsValid: true })
        {
            return "未设置";
        }

        var parts = new List<string>(5);
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(FormatKey(gesture.VirtualKey));
        return string.Join(" + ", parts);
    }

    private static string FormatKey(uint virtualKey)
    {
        Key key = KeyInterop.KeyFromVirtualKey(checked((int)virtualKey));
        int keyValue = (int)key;
        if (keyValue >= (int)Key.D0 && keyValue <= (int)Key.D9)
        {
            return ((char)('0' + keyValue - (int)Key.D0)).ToString();
        }

        if (keyValue >= (int)Key.A && keyValue <= (int)Key.Z)
        {
            return key.ToString();
        }

        if (keyValue >= (int)Key.NumPad0 && keyValue <= (int)Key.NumPad9)
        {
            return $"Num {keyValue - (int)Key.NumPad0}";
        }

        return key switch
        {
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            _ => key.ToString()
        };
    }
}
