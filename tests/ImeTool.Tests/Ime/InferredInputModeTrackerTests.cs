using ImeTool.Ime;
using ImeTool.State;

namespace ImeTool.Tests.Ime;

public sealed class InferredInputModeTrackerTests
{
    [Fact]
    public void Standalone_Toggle_Overrides_A_Stuck_Reported_Mode()
    {
        var tracker = new InferredInputModeTracker();
        var window = new WindowKey(new IntPtr(1), 10);
        Assert.Equal(TextInputMode.Chinese, tracker.Resolve(window, TextInputMode.Chinese));

        Assert.Equal(TextInputMode.English, tracker.Toggle(window));

        Assert.Equal(TextInputMode.English, tracker.Resolve(window, TextInputMode.Chinese));
    }

    [Fact]
    public void A_Real_Reported_Mode_Change_Remains_Authoritative()
    {
        var tracker = new InferredInputModeTracker();
        var window = new WindowKey(new IntPtr(1), 10);
        tracker.Resolve(window, TextInputMode.Chinese);
        tracker.Toggle(window);

        Assert.Equal(TextInputMode.English, tracker.Resolve(window, TextInputMode.English));
        Assert.Equal(TextInputMode.Chinese, tracker.Resolve(window, TextInputMode.Chinese));
    }

    [Fact]
    public void Inferred_Modes_Are_Independent_Per_Window()
    {
        var tracker = new InferredInputModeTracker();
        var first = new WindowKey(new IntPtr(1), 10);
        var second = new WindowKey(new IntPtr(2), 10);
        tracker.Resolve(first, TextInputMode.Chinese);
        tracker.Resolve(second, TextInputMode.Chinese);

        tracker.Toggle(first);

        Assert.Equal(TextInputMode.English, tracker.Resolve(first, TextInputMode.Chinese));
        Assert.Equal(TextInputMode.Chinese, tracker.Resolve(second, TextInputMode.Chinese));
    }

    [Fact]
    public void Restored_Effective_Mode_Overrides_A_Stale_Reported_Mode()
    {
        var tracker = new InferredInputModeTracker();
        var window = new WindowKey(new IntPtr(1), 10);
        tracker.Resolve(window, TextInputMode.English);

        tracker.SetEffectiveMode(window, TextInputMode.Chinese);

        Assert.Equal(TextInputMode.Chinese, tracker.Resolve(window, TextInputMode.English));
        Assert.True(tracker.HasEffectiveOverride(window));
    }
}
