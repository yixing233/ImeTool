namespace ImeTool.Overlay;

public sealed class ControlSpaceDetector
{
    public const uint VkControl = 0x11;
    public const uint VkSpace = 0x20;
    public const uint VkLeftControl = 0xA2;
    public const uint VkRightControl = 0xA3;

    private readonly HashSet<uint> _keysDown = [];
    private bool _candidate;

    public bool Process(uint virtualKey, bool isKeyDown)
    {
        if (isKeyDown)
        {
            if (!_keysDown.Add(virtualKey))
            {
                return false;
            }

            if (virtualKey == VkSpace)
            {
                _candidate = HasControlDown() && _keysDown.All(IsControlOrSpace);
            }
            else if (!IsControl(virtualKey) && _candidate)
            {
                _candidate = false;
            }

            return false;
        }

        if (!_keysDown.Remove(virtualKey))
        {
            return false;
        }

        if (IsControl(virtualKey) && !HasControlDown())
        {
            _candidate = false;
            return false;
        }

        if (virtualKey != VkSpace)
        {
            return false;
        }

        bool detected = _candidate && HasControlDown();
        _candidate = false;
        return detected;
    }

    private bool HasControlDown() => _keysDown.Any(IsControl);

    private static bool IsControlOrSpace(uint virtualKey) =>
        IsControl(virtualKey) || virtualKey == VkSpace;

    private static bool IsControl(uint virtualKey) =>
        virtualKey is VkControl or VkLeftControl or VkRightControl;
}
