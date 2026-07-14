using ImeTool.Ime;
using ImeTool.Native;

namespace ImeTool.Tests.Ime;

public sealed class TextInputModeResolverTests
{
    [Fact]
    public void Closed_Ime_Is_English_Even_When_Conversion_Mode_Is_Native()
    {
        Assert.Equal(
            TextInputMode.English,
            TextInputModeResolver.Resolve(ImeOpenStatus.Closed, conversionModeKnown: true, NativeMethods.ImeCmodeNative));
    }

    [Fact]
    public void Open_Ime_With_Native_Conversion_Is_Chinese()
    {
        Assert.Equal(
            TextInputMode.Chinese,
            TextInputModeResolver.Resolve(ImeOpenStatus.Open, conversionModeKnown: true, NativeMethods.ImeCmodeNative));
    }

    [Fact]
    public void Open_Ime_With_Alphanumeric_Conversion_Is_English()
    {
        Assert.Equal(
            TextInputMode.English,
            TextInputModeResolver.Resolve(ImeOpenStatus.Open, conversionModeKnown: true, 0));
    }

    [Fact]
    public void Open_Ime_Without_Conversion_Data_Falls_Back_To_Chinese()
    {
        Assert.Equal(
            TextInputMode.Chinese,
            TextInputModeResolver.Resolve(ImeOpenStatus.Open, conversionModeKnown: false, 0));
    }
}
