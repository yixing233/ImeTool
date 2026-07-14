using ImeTool.Native;

namespace ImeTool.Caret;

public sealed class CaretSnapshotStabilizer
{
    private const int NearHorizontalPixels = 16;
    private const int NearVerticalPixels = 10;
    private CaretSnapshot? _stable;
    private CaretSource? _pendingSource;
    private int _pendingCount;

    public CaretSnapshot Stabilize(CaretSnapshot candidate)
    {
        if (!_stable.HasValue || !IsSameTarget(_stable.Value, candidate))
        {
            SetStable(candidate);
            return candidate;
        }

        CaretSnapshot stable = _stable.Value;
        if (stable.Source == candidate.Source || IsNearby(stable.ScreenRect, candidate.ScreenRect))
        {
            SetStable(candidate);
            return candidate;
        }

        if (_pendingSource != candidate.Source)
        {
            _pendingSource = candidate.Source;
            _pendingCount = 1;
            return stable;
        }

        _pendingCount++;
        if (_pendingCount < 2)
        {
            return stable;
        }

        SetStable(candidate);
        return candidate;
    }

    public void Reset()
    {
        _stable = null;
        _pendingSource = null;
        _pendingCount = 0;
    }

    private void SetStable(CaretSnapshot snapshot)
    {
        _stable = snapshot;
        _pendingSource = null;
        _pendingCount = 0;
    }

    private static bool IsSameTarget(CaretSnapshot left, CaretSnapshot right) =>
        left.FocusHwnd == right.FocusHwnd && left.CaretHwnd == right.CaretHwnd;

    private static bool IsNearby(NativeMethods.RECT left, NativeMethods.RECT right) =>
        Math.Abs(left.Left - right.Left) <= NearHorizontalPixels &&
        Math.Abs(left.Bottom - right.Bottom) <= NearVerticalPixels;
}
