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
        NativeMethods.RECT workArea) =>
        Calculate(
            caretRect,
            windowWidth,
            windowHeight,
            safePadding,
            safePadding,
            offsetX,
            offsetY,
            workArea);

    public static MarkerPlacementResult Calculate(
        NativeMethods.RECT caretRect,
        int windowWidth,
        int windowHeight,
        int safePaddingX,
        int safePaddingY,
        int offsetX,
        int offsetY,
        NativeMethods.RECT workArea)
    {
        windowWidth = Math.Max(1, windowWidth);
        windowHeight = Math.Max(1, windowHeight);
        safePaddingX = Math.Max(0, safePaddingX);
        safePaddingY = Math.Max(0, safePaddingY);

        int left = caretRect.Right + offsetX - safePaddingX;
        int top = caretRect.Bottom + offsetY - safePaddingY;

        int maximumLeft = Math.Max(workArea.Left, workArea.Right - windowWidth);
        int maximumTop = Math.Max(workArea.Top, workArea.Bottom - windowHeight);
        left = Math.Clamp(left, workArea.Left, maximumLeft);
        top = Math.Clamp(top, workArea.Top, maximumTop);

        return new MarkerPlacementResult(left, top, false, false);
    }

    public static bool IsVisualClearOfCaret(
        double windowLeft,
        double windowTop,
        int safePaddingX,
        int safePaddingY,
        NativeMethods.RECT caretRect)
    {
        double visualLeft = windowLeft + Math.Max(0, safePaddingX);
        double visualTop = windowTop + Math.Max(0, safePaddingY);
        return visualLeft >= caretRect.Right || visualTop >= caretRect.Bottom;
    }
}
