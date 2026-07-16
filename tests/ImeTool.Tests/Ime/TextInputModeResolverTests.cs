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

    [Fact]
    public void Default_Ime_Native_Conversion_Wins_Over_A_Stale_English_Context()
    {
        Assert.Equal(
            TextInputMode.Chinese,
            TextInputModeReadingResolver.Resolve(
                isChineseInputMethod: true,
                defaultImeConversionKnown: true,
                defaultImeConversionMode: NativeMethods.ImeCmodeNative,
                contextMode: TextInputMode.English,
                openStatus: ImeOpenStatus.Closed));
    }

    [Fact]
    public void Default_Ime_Alphanumeric_Conversion_Wins_Over_A_Stale_Chinese_Context()
    {
        Assert.Equal(
            TextInputMode.English,
            TextInputModeReadingResolver.Resolve(
                isChineseInputMethod: true,
                defaultImeConversionKnown: true,
                defaultImeConversionMode: 0,
                contextMode: TextInputMode.Chinese,
                openStatus: ImeOpenStatus.Open));
    }

    [Fact]
    public void Non_Chinese_Layout_Ignores_Default_Ime_Conversion()
    {
        Assert.Equal(
            TextInputMode.English,
            TextInputModeReadingResolver.Resolve(
                isChineseInputMethod: false,
                defaultImeConversionKnown: true,
                defaultImeConversionMode: NativeMethods.ImeCmodeNative,
                contextMode: TextInputMode.English,
                openStatus: ImeOpenStatus.Closed));
    }

    [Fact]
    public void Missing_Default_Ime_Conversion_Uses_Context_Mode()
    {
        Assert.Equal(
            TextInputMode.Chinese,
            TextInputModeReadingResolver.Resolve(
                isChineseInputMethod: true,
                defaultImeConversionKnown: false,
                defaultImeConversionMode: 0,
                contextMode: TextInputMode.Chinese,
                openStatus: ImeOpenStatus.Closed));
    }
}
