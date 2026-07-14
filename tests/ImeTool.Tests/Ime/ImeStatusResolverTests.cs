using ImeTool.Ime;

namespace ImeTool.Tests.Ime;

public sealed class ImeStatusResolverTests
{
    [Fact]
    public void FirstKnown_Uses_First_Available_Fallback_When_Primary_Is_Unknown()
    {
        ImeOpenStatus result = ImeStatusResolver.FirstKnown(
            () => ImeOpenStatus.Unknown,
            () => ImeOpenStatus.Open,
            () => ImeOpenStatus.Closed);

        Assert.Equal(ImeOpenStatus.Open, result);
    }

    [Fact]
    public void FirstKnown_Does_Not_Invoke_Later_Backends_After_Success()
    {
        bool laterCalled = false;

        ImeOpenStatus result = ImeStatusResolver.FirstKnown(
            () => ImeOpenStatus.Closed,
            () =>
            {
                laterCalled = true;
                return ImeOpenStatus.Open;
            });

        Assert.Equal(ImeOpenStatus.Closed, result);
        Assert.False(laterCalled);
    }

    [Fact]
    public void FirstSuccessful_Falls_Through_Failed_Writers()
    {
        int calls = 0;

        bool result = ImeStatusResolver.FirstSuccessful(
            () =>
            {
                calls++;
                return false;
            },
            () =>
            {
                calls++;
                return true;
            });

        Assert.True(result);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(0x0804, true)]
    [InlineData(0x0404, true)]
    [InlineData(0x1004, true)]
    [InlineData(0x0409, false)]
    [InlineData(0x0411, false)]
    public void Chinese_Language_Id_Is_Detected(int languageId, bool expected)
    {
        Assert.Equal(expected, TsfImeService.IsChineseLanguageId((ushort)languageId));
    }
}
