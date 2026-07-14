namespace ImeTool.Overlay;

public sealed class StandaloneShiftDetector
{
    public const uint VkShift = 0x10;
    public const uint VkLeftShift = 0xA0;
    public const uint VkRightShift = 0xA1;
    public const long MaximumTapDurationMilliseconds = 900;

    private readonly HashSet<uint> _keysDown = [];
    private bool _candidate;
    private long _startedAt;

    public bool Process(uint virtualKey, bool isKeyDown, long timestampMilliseconds)
    {
        bool shift = IsShift(virtualKey);
        if (isKeyDown)
        {
            if (_keysDown.Contains(virtualKey))
            {
                return false;
            }

            if (shift && !HasShiftDown())
            {
                _candidate = _keysDown.Count == 0;
                _startedAt = timestampMilliseconds;
            }
            else if (!shift && HasShiftDown())
            {
                _candidate = false;
            }

            _keysDown.Add(virtualKey);
            return false;
        }

        _keysDown.Remove(virtualKey);
        if (!shift || HasShiftDown())
        {
            return false;
        }

        long duration = timestampMilliseconds - _startedAt;
        bool detected = _candidate && duration >= 0 && duration <= MaximumTapDurationMilliseconds;
        _candidate = false;
        return detected;
    }

    private bool HasShiftDown() => _keysDown.Any(IsShift);

    private static bool IsShift(uint virtualKey) =>
        virtualKey is VkShift or VkLeftShift or VkRightShift;
}
