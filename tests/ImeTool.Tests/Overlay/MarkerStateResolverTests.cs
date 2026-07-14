using ImeTool.Ime;
using ImeTool.Overlay;

namespace ImeTool.Tests.Overlay;

public sealed class MarkerStateResolverTests
{
    [Theory]
    [InlineData(ImeOpenStatus.Open, MarkerState.Chinese)]
    [InlineData(ImeOpenStatus.Closed, MarkerState.English)]
    [InlineData(ImeOpenStatus.Unknown, MarkerState.Unknown)]
    public void Uses_Ime_Status_When_Caps_Lock_Is_Off(ImeOpenStatus imeStatus, MarkerState expected)
    {
        Assert.Equal(expected, MarkerStateResolver.Resolve(imeStatus, capsLockOn: false));
    }

    [Theory]
    [InlineData(ImeOpenStatus.Open)]
    [InlineData(ImeOpenStatus.Closed)]
    [InlineData(ImeOpenStatus.Unknown)]
    public void Caps_Lock_Takes_Priority_Over_Ime_Status(ImeOpenStatus imeStatus)
    {
        Assert.Equal(MarkerState.CapsLock, MarkerStateResolver.Resolve(imeStatus, capsLockOn: true));
    }

    [Theory]
    [InlineData(TextInputMode.Chinese, MarkerState.Chinese)]
    [InlineData(TextInputMode.English, MarkerState.English)]
    [InlineData(TextInputMode.Unknown, MarkerState.Unknown)]
    public void Uses_Detailed_Input_Mode_When_Caps_Lock_Is_Off(TextInputMode inputMode, MarkerState expected)
    {
        Assert.Equal(expected, MarkerStateResolver.Resolve(inputMode, capsLockOn: false));
    }
}
