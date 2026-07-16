using ImeTool.Native;

namespace ImeTool.Caret;

public readonly record struct UiAutomationCaretReadResult(
    bool Found,
    CaretSnapshot Snapshot,
    bool TrustNativeCaret,
    IntPtr FocusHwnd,
    string? FailureReason,
    NativeMethods.RECT? NativeCaretBoundsHint)
{
    public static UiAutomationCaretReadResult Success(CaretSnapshot snapshot) =>
        new(true, snapshot, true, snapshot.FocusHwnd, null, null);

    public static UiAutomationCaretReadResult Failure(
        string failureReason,
        bool trustNativeCaret = false,
        IntPtr focusHwnd = default,
        NativeMethods.RECT? nativeCaretBoundsHint = null) =>
        new(false, default, trustNativeCaret, focusHwnd, failureReason, nativeCaretBoundsHint);
}

public sealed class UiAutomationCaretReader : IDisposable
{
    private const long CacheLifetimeMilliseconds = 3000;
    private const long RefreshIntervalMilliseconds = 40;

    private readonly object _syncRoot = new();
    private readonly Func<IntPtr, UiAutomationCaretReadResult> _read;
    private readonly string _operationName;
    private readonly AutoResetEvent _requestAvailable = new(false);
    private readonly Thread _worker;
    private int _generation;
    private long _lastQueuedTimestamp;
    private bool _hasPendingRequest;
    private IntPtr _pendingForeground;
    private int _pendingGeneration;
    private long _completedTimestamp;
    private IntPtr _completedForeground;
    private UiAutomationCaretReadResult _completedResult;
    private bool _disposed;

    public UiAutomationCaretReader(
        Func<IntPtr, UiAutomationCaretReadResult> read,
        string operationName = "UI Automation")
    {
        _read = read;
        _operationName = operationName;
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = $"ImeTool.{operationName.Replace(" ", string.Empty)}Caret"
        };
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();
    }

    public bool TryGet(
        IntPtr foregroundHwnd,
        out CaretSnapshot snapshot,
        out string? failureReason)
    {
        bool hasResult = TryGetResult(foregroundHwnd, out UiAutomationCaretReadResult result);
        failureReason = hasResult
            ? result.FailureReason
            : $"{_operationName} caret lookup is pending.";
        snapshot = hasResult && result.Found ? result.Snapshot : default;
        return hasResult && result.Found;
    }

    public bool TryGetResult(
        IntPtr foregroundHwnd,
        out UiAutomationCaretReadResult result)
    {
        result = default;
        bool signalWorker = false;
        long now = Environment.TickCount64;
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return false;
            }

            if (now - _lastQueuedTimestamp >= RefreshIntervalMilliseconds)
            {
                _lastQueuedTimestamp = now;
                _pendingForeground = foregroundHwnd;
                _pendingGeneration = _generation;
                _hasPendingRequest = true;
                signalWorker = true;
            }

            bool hasRecentResult = _completedForeground == foregroundHwnd &&
                                   now - _completedTimestamp <= CacheLifetimeMilliseconds;
            if (hasRecentResult)
            {
                result = _completedResult;
            }
            else
            {
                if (signalWorker)
                {
                    _requestAvailable.Set();
                }

                return false;
            }
        }

        if (signalWorker)
        {
            _requestAvailable.Set();
        }

        return true;
    }

    public void Invalidate()
    {
        lock (_syncRoot)
        {
            _generation++;
            _lastQueuedTimestamp = 0;
            _hasPendingRequest = false;
            _completedTimestamp = 0;
            _completedForeground = IntPtr.Zero;
            _completedResult = default;
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _hasPendingRequest = false;
        }

        _requestAvailable.Set();
        _worker.Join(TimeSpan.FromMilliseconds(250));
    }

    private void WorkerLoop()
    {
        while (true)
        {
            _requestAvailable.WaitOne();
            while (TryTakePendingRequest(out IntPtr foregroundHwnd, out int generation))
            {
                UiAutomationCaretReadResult result;
                try
                {
                    result = _read(foregroundHwnd);
                }
                catch (Exception exception)
                {
                    result = UiAutomationCaretReadResult.Failure(
                        $"{_operationName} failed with {exception.GetType().Name}: {exception.Message}",
                        trustNativeCaret: true,
                        focusHwnd: foregroundHwnd);
                }

                StoreResult(foregroundHwnd, generation, result);
            }

            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }
            }
        }
    }

    private bool TryTakePendingRequest(out IntPtr foregroundHwnd, out int generation)
    {
        lock (_syncRoot)
        {
            if (_disposed || !_hasPendingRequest)
            {
                foregroundHwnd = IntPtr.Zero;
                generation = 0;
                return false;
            }

            foregroundHwnd = _pendingForeground;
            generation = _pendingGeneration;
            _hasPendingRequest = false;
            return true;
        }
    }

    private void StoreResult(
        IntPtr foregroundHwnd,
        int generation,
        UiAutomationCaretReadResult result)
    {
        lock (_syncRoot)
        {
            if (_disposed || generation != _generation)
            {
                return;
            }

            _completedForeground = foregroundHwnd;
            _completedTimestamp = Environment.TickCount64;
            _completedResult = result;
        }
    }
}
