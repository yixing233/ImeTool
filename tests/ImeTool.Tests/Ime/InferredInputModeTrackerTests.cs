using ImeTool.Ime;
using ImeTool.State;

namespace ImeTool.Tests.Ime;

public sealed class InferredInputModeTrackerTests
{
    [Fact]
    public void Unchanged_Reported_Mode_Is_Not_Inverted()
    {
        var tracker = new InferredInputModeTracker();
        var window = new WindowKey(new IntPtr(1), 10);

        Assert.Equal(TextInputMode.English, tracker.Resolve(window, TextInputMode.English));
        Assert.Equal(TextInputMode.English, tracker.Resolve(window, TextInputMode.English));
        Assert.False(tracker.HasEffectiveOverride(window));
    }

    [Fact]
    public void A_Real_Reported_Mode_Change_Remains_Authoritative()
    {
        var tracker = new InferredInputModeTracker();
        var window = new WindowKey(new IntPtr(1), 10);
        tracker.Resolve(window, TextInputMode.English);

        Assert.Equal(TextInputMode.Chinese, tracker.Resolve(window, TextInputMode.Chinese));
        Assert.False(tracker.HasEffectiveOverride(window));
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

    [Fact]
    public void Restored_Modes_Are_Independent_Per_Window()
    {
        var tracker = new InferredInputModeTracker();
        var first = new WindowKey(new IntPtr(1), 10);
        var second = new WindowKey(new IntPtr(2), 10);
        tracker.Resolve(first, TextInputMode.English);
        tracker.Resolve(second, TextInputMode.English);

        tracker.SetEffectiveMode(first, TextInputMode.Chinese);

        Assert.Equal(TextInputMode.Chinese, tracker.Resolve(first, TextInputMode.English));
        Assert.Equal(TextInputMode.English, tracker.Resolve(second, TextInputMode.English));
    }

    [Fact]
    public void Real_Reported_Change_Clears_A_Restore_Override()
    {
        var tracker = new InferredInputModeTracker();
        var window = new WindowKey(new IntPtr(1), 10);
        tracker.Resolve(window, TextInputMode.English);
        tracker.SetEffectiveMode(window, TextInputMode.Chinese);

        Assert.Equal(TextInputMode.Chinese, tracker.Resolve(window, TextInputMode.Chinese));
        Assert.False(tracker.HasEffectiveOverride(window));
    }
}
