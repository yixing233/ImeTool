using ImeTool.Settings;

namespace ImeTool.Tests.Settings;

public sealed class MarkerAppearanceSettingsTests
{
    [Fact]
    public void Default_Settings_Use_Text_Marker_With_Default_Chinese_And_English_Colors()
    {
        AppSettings settings = new AppSettings().Normalize();

        Assert.Equal(MarkerStyle.Text, settings.Marker.Style);
        Assert.Equal(12, settings.Marker.Size);
        Assert.Equal(6, settings.Marker.OffsetX);
        Assert.Equal(6, settings.Marker.OffsetY);
        Assert.Equal("#EF4444", settings.Marker.ChineseColor);
        Assert.Equal("#2563EB", settings.Marker.EnglishColor);
        Assert.Equal("#F59E0B", settings.Marker.CapsLockColor);
        Assert.Equal("A", settings.Marker.CapsLockText);
    }

    [Fact]
    public void Normalize_Clamps_Size_And_Offsets_And_Normalizes_Color()
    {
        var marker = new MarkerAppearanceSettings
        {
            Size = 999,
            OffsetX = -999,
            OffsetY = 999,
            ChineseColor = "ff0000",
            EnglishColor = "not-a-color",
            CapsLockColor = "00ff00",
            ChineseText = "  ",
            EnglishText = "  E  ",
            CapsLockText = "  "
        }.Normalize();

        Assert.Equal(96, marker.Size);
        Assert.Equal(-96, marker.OffsetX);
        Assert.Equal(96, marker.OffsetY);
        Assert.Equal("#FF0000", marker.ChineseColor);
        Assert.Equal("#2563EB", marker.EnglishColor);
        Assert.Equal("#00FF00", marker.CapsLockColor);
        Assert.Equal("中", marker.ChineseText);
        Assert.Equal("E", marker.EnglishText);
        Assert.Equal("A", marker.CapsLockText);
    }

    [Fact]
    public void SettingsService_RoundTrips_Custom_Image_Marker_Settings()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        var service = new SettingsService(path);
        var expected = new AppSettings
        {
            Enabled = true,
            StartWithWindows = false,
            Marker = new MarkerAppearanceSettings
            {
                Style = MarkerStyle.Image,
                Size = 32,
                OffsetX = 4,
                OffsetY = -2,
                ChineseImagePath = @"E:\Images\zh.png",
                EnglishImagePath = @"E:\Images\en.png",
                CapsLockImagePath = @"E:\Images\caps.png"
            }
        };

        service.Save(expected);
        AppSettings actual = service.Load();

        Assert.Equal(MarkerStyle.Image, actual.Marker.Style);
        Assert.Equal(32, actual.Marker.Size);
        Assert.Equal(4, actual.Marker.OffsetX);
        Assert.Equal(-2, actual.Marker.OffsetY);
        Assert.Equal(@"E:\Images\zh.png", actual.Marker.ChineseImagePath);
        Assert.Equal(@"E:\Images\en.png", actual.Marker.EnglishImagePath);
        Assert.Equal(@"E:\Images\caps.png", actual.Marker.CapsLockImagePath);
    }
}

