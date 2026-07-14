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
        int visualWidth = Math.Max(1, windowWidth - safePadding * 2);
        int visualHeight = Math.Max(1, windowHeight - safePadding * 2);

        int left = caretRect.Left + offsetX - safePadding;
        int top = caretRect.Bottom + offsetY - safePadding;
        bool flipHorizontal = left + windowWidth > workArea.Right;
        bool flipVertical = top + windowHeight > workArea.Bottom;

        if (flipHorizontal)
        {
            int gap = Math.Abs(offsetX);
            left = caretRect.Left - gap - visualWidth - safePadding;
        }

        if (flipVertical)
        {
            int gap = Math.Abs(offsetY);
            top = caretRect.Top - gap - visualHeight - safePadding;
        }

        int maximumLeft = Math.Max(workArea.Left, workArea.Right - windowWidth);
        int maximumTop = Math.Max(workArea.Top, workArea.Bottom - windowHeight);
        left = Math.Clamp(left, workArea.Left, maximumLeft);
        top = Math.Clamp(top, workArea.Top, maximumTop);

        return new MarkerPlacementResult(left, top, flipHorizontal, flipVertical);
    }
}
