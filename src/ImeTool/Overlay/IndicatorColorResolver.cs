using ImeTool.Settings;

namespace ImeTool.Overlay;

public static class IndicatorColorResolver
{
    public static string GetColor(MarkerState state, MarkerAppearanceSettings settings) => state switch
    {
        MarkerState.Chinese => settings.ChineseColor,
        MarkerState.CapsLock => settings.CapsLockColor,
        _ => settings.EnglishColor
    };
}
