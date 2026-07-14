namespace ImeTool.State;

public sealed class WindowStateStore
{
    private readonly Dictionary<WindowKey, bool> _states = new();

    public int Count => _states.Count;

    public void Save(WindowKey key, bool isImeOpen)
    {
        if (key.Hwnd == IntPtr.Zero || key.ProcessId == 0)
        {
            return;
        }

        _states[key] = isImeOpen;
    }

    public bool TryGet(WindowKey key, out bool isImeOpen) => _states.TryGetValue(key, out isImeOpen);

    public bool Remove(WindowKey key) => _states.Remove(key);

    public void Clear() => _states.Clear();

    public int Prune(Func<IntPtr, bool> isAlive)
    {
        List<WindowKey>? dead = null;
        foreach (WindowKey key in _states.Keys)
        {
            if (!isAlive(key.Hwnd))
            {
                dead ??= [];
                dead.Add(key);
            }
        }

        if (dead is null)
        {
            return 0;
        }

        foreach (WindowKey key in dead)
        {
            _states.Remove(key);
        }

        return dead.Count;
    }
}
