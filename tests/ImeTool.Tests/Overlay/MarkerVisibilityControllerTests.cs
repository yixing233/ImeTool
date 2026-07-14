using ImeTool.Native;
using ImeTool.Overlay;
using ImeTool.Settings;

namespace ImeTool.Tests.Overlay;

public sealed class MarkerVisibilityControllerTests
{
    private static readonly NativeMethods.RECT Caret = new()
    {
        Left = 100,
        Top = 100,
        Right = 102,
        Bottom = 120
    };

    [Fact]
    public void Always_Mode_Remains_Visible_After_Delay()
    {
        var controller = new MarkerVisibilityController();
        var settings = new MarkerBehaviorSettings
        {
            DisplayMode = MarkerDisplayMode.Always,
            AutoHideDelayMilliseconds = 300
        };
        DateTimeOffset start = DateTimeOffset.UnixEpoch;

        Assert.True(controller.ShouldShow(start, settings, new IntPtr(1), MarkerState.English, Caret));
        Assert.True(controller.ShouldShow(start.AddSeconds(30), settings, new IntPtr(1), MarkerState.English, Caret));
    }

    [Fact]
    public void Ime_Change_Mode_Hides_After_Idle_And_Reappears_On_Change()
    {
        var controller = new MarkerVisibilityController();
        var settings = new MarkerBehaviorSettings
        {
            DisplayMode = MarkerDisplayMode.OnImeChange,
            AutoHideDelayMilliseconds = 500
        };
        DateTimeOffset start = DateTimeOffset.UnixEpoch;

        Assert.True(controller.ShouldShow(start, settings, new IntPtr(1), MarkerState.English, Caret));
        Assert.False(controller.ShouldShow(start.AddMilliseconds(500), settings, new IntPtr(1), MarkerState.English, Caret));
        Assert.True(controller.ShouldShow(start.AddMilliseconds(700), settings, new IntPtr(1), MarkerState.Chinese, Caret));
    }

    [Fact]
    public void While_Typing_Mode_Extends_Visibility_When_Caret_Moves()
    {
        var controller = new MarkerVisibilityController();
        var settings = new MarkerBehaviorSettings
        {
            DisplayMode = MarkerDisplayMode.WhileTyping,
            AutoHideDelayMilliseconds = 500
        };
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        NativeMethods.RECT moved = Caret;
        moved.Left += 8;
        moved.Right += 8;

        Assert.True(controller.ShouldShow(start, settings, new IntPtr(1), MarkerState.English, Caret));
        Assert.True(controller.ShouldShow(start.AddMilliseconds(400), settings, new IntPtr(1), MarkerState.English, moved));
        Assert.True(controller.ShouldShow(start.AddMilliseconds(800), settings, new IntPtr(1), MarkerState.English, moved));
        Assert.False(controller.ShouldShow(start.AddMilliseconds(900), settings, new IntPtr(1), MarkerState.English, moved));
    }

    [Fact]
    public void Reset_Makes_Next_Observation_Visible()
    {
        var controller = new MarkerVisibilityController();
        var settings = new MarkerBehaviorSettings
        {
            DisplayMode = MarkerDisplayMode.OnImeChange,
            AutoHideDelayMilliseconds = 300
        };
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        controller.ShouldShow(start, settings, new IntPtr(1), MarkerState.English, Caret);
        Assert.False(controller.ShouldShow(start.AddSeconds(1), settings, new IntPtr(1), MarkerState.English, Caret));

        controller.Reset();

        Assert.True(controller.ShouldShow(start.AddSeconds(1), settings, new IntPtr(1), MarkerState.English, Caret));
    }

    [Fact]
    public void Ime_Change_Mode_Reappears_When_Caps_Lock_Changes()
    {
        var controller = new MarkerVisibilityController();
        var settings = new MarkerBehaviorSettings
        {
            DisplayMode = MarkerDisplayMode.OnImeChange,
            AutoHideDelayMilliseconds = 300
        };
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        controller.ShouldShow(start, settings, new IntPtr(1), MarkerState.English, Caret);
        Assert.False(controller.ShouldShow(start.AddSeconds(1), settings, new IntPtr(1), MarkerState.English, Caret));

        Assert.True(controller.ShouldShow(start.AddSeconds(1), settings, new IntPtr(1), MarkerState.CapsLock, Caret));
    }
}
