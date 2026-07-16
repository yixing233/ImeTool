using ImeTool.Ime;
using ImeTool.Native;

namespace ImeTool.Tests.Ime;

public sealed class ImeModeSignalTrackerTests
{
    [Fact]
    public void Unchanged_State_Codes_Do_Not_Toggle_The_Result()
    {
        var tracker = new ImeModeSignalTracker();
        IntPtr hwnd = new(1);

        Assert.Equal(TextInputMode.English, Resolve(tracker, hwnd, ImeOpenStatus.Open, 0));
        Assert.Equal(TextInputMode.English, Resolve(tracker, hwnd, ImeOpenStatus.Open, 0));
    }

    [Fact]
    public void Conversion_Only_Change_Selects_Conversion_Signal()
    {
        var tracker = new ImeModeSignalTracker();
        IntPtr hwnd = new(1);
        Resolve(tracker, hwnd, ImeOpenStatus.Open, 0);

        Assert.Equal(
            TextInputMode.Chinese,
            Resolve(tracker, hwnd, ImeOpenStatus.Open, NativeMethods.ImeCmodeNative));
    }

    [Fact]
    public void Open_Status_Only_Change_Selects_Open_Status_Signal()
    {
        var tracker = new ImeModeSignalTracker();
        IntPtr hwnd = new(1);
        Resolve(tracker, hwnd, ImeOpenStatus.Open, NativeMethods.ImeCmodeNative);

        Assert.Equal(
            TextInputMode.English,
            Resolve(tracker, hwnd, ImeOpenStatus.Closed, NativeMethods.ImeCmodeNative));
    }

    [Fact]
    public void Later_Conversion_Change_Can_Replace_Open_Status_Strategy()
    {
        var tracker = new ImeModeSignalTracker();
        IntPtr hwnd = new(1);
        Resolve(tracker, hwnd, ImeOpenStatus.Open, NativeMethods.ImeCmodeNative);
        Resolve(tracker, hwnd, ImeOpenStatus.Closed, NativeMethods.ImeCmodeNative);

        Assert.Equal(TextInputMode.English, Resolve(tracker, hwnd, ImeOpenStatus.Closed, 0));
        Assert.Equal(
            TextInputMode.Chinese,
            Resolve(tracker, hwnd, ImeOpenStatus.Closed, NativeMethods.ImeCmodeNative));
    }

    [Fact]
    public void Missing_Raw_Signal_Uses_Fallback_Without_Inference()
    {
        var tracker = new ImeModeSignalTracker();

        Assert.Equal(
            TextInputMode.Chinese,
            tracker.Resolve(
                new IntPtr(1),
                openStatusKnown: false,
                ImeOpenStatus.Unknown,
                conversionModeKnown: true,
                conversionMode: 0,
                fallbackMode: TextInputMode.Chinese));
    }

    private static TextInputMode Resolve(
        ImeModeSignalTracker tracker,
        IntPtr hwnd,
        ImeOpenStatus openStatus,
        uint conversionMode) =>
        tracker.Resolve(
            hwnd,
            openStatusKnown: true,
            openStatus,
            conversionModeKnown: true,
            conversionMode,
            fallbackMode: TextInputMode.Unknown);
}
