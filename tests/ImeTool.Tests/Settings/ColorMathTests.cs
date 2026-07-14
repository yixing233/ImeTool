using ImeTool.Settings;
using MediaColor = System.Windows.Media.Color;

namespace ImeTool.Tests.Settings;

public sealed class ColorMathTests
{
    [Fact]
    public void Hex_RoundTrips_Without_Alpha()
    {
        Assert.True(ColorMath.TryParseHex("#2A6FDB", out MediaColor color));

        Assert.Equal(MediaColor.FromRgb(0x2A, 0x6F, 0xDB), color);
        Assert.Equal("#2A6FDB", ColorMath.ToHex(color));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#GG0000")]
    public void TryParseHex_Rejects_Invalid_Values(string? value)
    {
        Assert.False(ColorMath.TryParseHex(value, out _));
    }

    [Theory]
    [InlineData(0, 255, 0, 0)]
    [InlineData(120, 0, 255, 0)]
    [InlineData(240, 0, 0, 255)]
    public void Hsv_Produces_Primary_Colors(double hue, byte red, byte green, byte blue)
    {
        Assert.Equal(MediaColor.FromRgb(red, green, blue), ColorMath.FromHsv(hue, 1, 1));
    }

    [Theory]
    [InlineData(239, 68, 68)]
    [InlineData(37, 99, 235)]
    [InlineData(20, 184, 166)]
    [InlineData(100, 116, 139)]
    public void Rgb_Hsv_RoundTrip_Preserves_Color(byte red, byte green, byte blue)
    {
        MediaColor original = MediaColor.FromRgb(red, green, blue);
        (double hue, double saturation, double value) = ColorMath.ToHsv(original);

        Assert.Equal(original, ColorMath.FromHsv(hue, saturation, value));
    }
}
