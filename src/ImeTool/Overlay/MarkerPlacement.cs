using ImeTool.Native;

namespace ImeTool.Overlay;

public readonly record struct MarkerPlacementResult(
    int Left,
    int Top,
    bool FlippedHorizontal,
    bool FlippedVertical);

public static class MarkerPlacement
{
    public static MarkerPlacementResult Calculate(
        NativeMethods.RECT caretRect,
        int windowWidth,
        int windowHeight,
        int safePadding,
        int offsetX,
        int offsetY,
        NativeMethods.RECT workArea)
    {
        windowWidth = Math.Max(1, windowWidth);
        windowHeight = Math.Max(1, windowHeight);
        safePadding = Math.Max(0, safePadding);

        int left = caretRect.Right + offsetX - safePadding;
        int top = caretRect.Bottom + offsetY - safePadding;

        int maximumLeft = Math.Max(workArea.Left, workArea.Right - windowWidth);
        int maximumTop = Math.Max(workArea.Top, workArea.Bottom - windowHeight);
        left = Math.Clamp(left, workArea.Left, maximumLeft);
        top = Math.Clamp(top, workArea.Top, maximumTop);

        return new MarkerPlacementResult(left, top, false, false);
    }
}
