using ImeTool.Hotkeys;
using ImeTool.Settings;

namespace ImeTool.Tests.Hotkeys;

public sealed class HotkeyGestureFormatterTests
{
    [Fact]
    public void Formats_Modifiers_In_A_Stable_Order()
    {
        var gesture = new HotkeyGestureSettings
        {
            Modifiers = HotkeyModifiers.Windows | HotkeyModifiers.Shift | HotkeyModifiers.Control | HotkeyModifiers.Alt,
            VirtualKey = 0x4B
        };

        Assert.Equal("Ctrl + Alt + Shift + Win + K", HotkeyGestureFormatter.Format(gesture));
    }

    [Fact]
    public void Removed_Gesture_Is_Shown_As_Not_Configured()
    {
        Assert.Equal("未设置", HotkeyGestureFormatter.Format(null));
    }
}
