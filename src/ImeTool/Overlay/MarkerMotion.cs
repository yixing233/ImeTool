namespace ImeTool.Overlay;

public static class MarkerMotion
{
    public const double MaximumAnimatedDistancePixels = 420;
    public const double VerticalJitterToleranceDip = 4;

    public static double Distance(double fromX, double fromY, double toX, double toY)
    {
        double deltaX = toX - fromX;
        double deltaY = toY - fromY;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    public static bool ShouldAnimate(double fromX, double fromY, double toX, double toY)
    {
        double distance = Distance(fromX, fromY, toX, toY);
        return distance > 1 && distance <= MaximumAnimatedDistancePixels;
    }

    public static double DurationMilliseconds(double distancePixels) =>
        Math.Clamp(78 + distancePixels * 0.18, 82, 125);

    public static double DurationMilliseconds(double distancePixels, int preferredDurationMilliseconds)
    {
        double duration = Math.Clamp(preferredDurationMilliseconds, 40, 300);
        double distanceFactor = 0.85 + Math.Min(Math.Max(distancePixels, 0), 240) / 800;
        return Math.Clamp(duration * distanceFactor, 40, 360);
    }

    public static double EaseOutCubic(double progress)
    {
        double clamped = Math.Clamp(progress, 0, 1);
        return 1 - Math.Pow(1 - clamped, 3);
    }

    public static double Interpolate(double from, double to, double progress) =>
        from + (to - from) * EaseOutCubic(progress);

    public static double StabilizeTarget(double previous, double candidate, double tolerancePixels) =>
        Math.Abs(candidate - previous) <= Math.Max(0, tolerancePixels)
            ? previous
            : candidate;
}
