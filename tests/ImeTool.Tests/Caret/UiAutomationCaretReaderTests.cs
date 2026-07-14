using System.Diagnostics;
using ImeTool.Caret;
using ImeTool.Native;

namespace ImeTool.Tests.Caret;

public sealed class UiAutomationCaretReaderTests
{
    [Fact]
    public void TryGet_DoesNotBlockWhileProviderIsReading()
    {
        using var gate = new ManualResetEventSlim(false);
        using var reader = new UiAutomationCaretReader(_ =>
        {
            gate.Wait();
            return UiAutomationCaretReadResult.Success(Snapshot(120, 240));
        });

        var stopwatch = Stopwatch.StartNew();
        bool found = reader.TryGet(new IntPtr(1), out _, out _);
        stopwatch.Stop();

        Assert.False(found);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(100));
        gate.Set();
    }

    [Fact]
    public async Task CompletedExactCaret_IsReturnedFromCache()
    {
        using var reader = new UiAutomationCaretReader(_ =>
            UiAutomationCaretReadResult.Success(Snapshot(120, 240)));

        Assert.False(reader.TryGet(new IntPtr(1), out _, out _));
        CaretSnapshot result = default;
        bool found = false;
        for (int attempt = 0; attempt < 50 && !found; attempt++)
        {
            await Task.Delay(10);
            found = reader.TryGet(new IntPtr(1), out result, out _);
        }

        Assert.True(found);
        Assert.Equal(120, result.ScreenRect.Left);
    }

    [Fact]
    public async Task Invalidate_DropsCachedCaretImmediately()
    {
        using var gate = new ManualResetEventSlim(true);
        using var reader = new UiAutomationCaretReader(_ =>
        {
            gate.Wait();
            return UiAutomationCaretReadResult.Success(Snapshot(120, 240));
        });
        reader.TryGet(new IntPtr(1), out _, out _);
        for (int attempt = 0; attempt < 50 && !reader.TryGet(new IntPtr(1), out _, out _); attempt++)
        {
            await Task.Delay(10);
        }

        gate.Reset();
        reader.Invalidate();

        Assert.False(reader.TryGet(new IntPtr(1), out _, out _));
        gate.Set();
    }

    [Fact]
    public async Task FocusInvalidation_CoalescesRequestsOnOneWorker()
    {
        using var firstReadGate = new ManualResetEventSlim(false);
        int calls = 0;
        int activeCalls = 0;
        int maximumConcurrentCalls = 0;
        using var reader = new UiAutomationCaretReader(foreground =>
        {
            int active = Interlocked.Increment(ref activeCalls);
            UpdateMaximum(ref maximumConcurrentCalls, active);
            int call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                firstReadGate.Wait();
            }

            Interlocked.Decrement(ref activeCalls);
            return UiAutomationCaretReadResult.Success(
                Snapshot(foreground.ToInt32() * 100, 240));
        });

        reader.TryGet(new IntPtr(1), out _, out _);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref calls) == 1, 1000));
        for (int focus = 2; focus <= 10; focus++)
        {
            reader.Invalidate();
            reader.TryGet(new IntPtr(focus), out _, out _);
        }

        await Task.Delay(50);
        Assert.Equal(1, Volatile.Read(ref calls));
        firstReadGate.Set();
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref calls) >= 2, 1000));
        Assert.Equal(1, Volatile.Read(ref maximumConcurrentCalls));
    }

    private static void UpdateMaximum(ref int target, int candidate)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
            if (candidate <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, candidate, current) != current);
    }

    private static CaretSnapshot Snapshot(int left, int bottom) => new(
        new IntPtr(10),
        new IntPtr(10),
        new NativeMethods.RECT
        {
            Left = left,
            Top = bottom - 20,
            Right = left + 1,
            Bottom = bottom
        },
        CaretSource.UiAutomationTextPattern);
}
