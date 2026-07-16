using ImeTool.Settings;

namespace ImeTool.Tests.Settings;

public sealed class AdditionalIndicatorSettingsTests
{
    [Fact]
    public void Defaults_Are_Disabled()
    {
        var settings = new AdditionalIndicatorSettings();

        Assert.False(settings.EnableWindowBorder);
        Assert.False(settings.EnableMouseMarker);
        Assert.False(settings.ColorizeIBeamCursor);
    }

    [Fact]
    public void Normalize_Clamps_Width_And_Mouse_Offsets()
    {
        AdditionalIndicatorSettings settings = new AdditionalIndicatorSettings
        {
            WindowBorderWidth = 99,
            MouseOffsetX = -999,
            MouseOffsetY = 999
        }.Normalize();

        Assert.Equal(12, settings.WindowBorderWidth);
        Assert.Equal(-96, settings.MouseOffsetX);
        Assert.Equal(96, settings.MouseOffsetY);
    }

    [Fact]
    public void AppSettings_Normalize_Preserves_Enabled_Indicators()
    {
        AppSettings settings = new AppSettings
        {
            AdditionalIndicators = new AdditionalIndicatorSettings
            {
                EnableWindowBorder = true,
                EnableMouseMarker = true,
                ColorizeIBeamCursor = true
            }
        }.Normalize();

        Assert.True(settings.AdditionalIndicators.EnableWindowBorder);
        Assert.True(settings.AdditionalIndicators.EnableMouseMarker);
        Assert.True(settings.AdditionalIndicators.ColorizeIBeamCursor);
    }
}
