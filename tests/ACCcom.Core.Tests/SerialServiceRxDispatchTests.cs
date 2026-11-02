using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

/// <summary>
/// Locks the RX dispatch contract: <see cref="SerialService"/> reads bytes on
/// the SerialPort.DataReceived thread but hands entries to subscribers from a
/// single-consumer drain task, so a slow subscriber (HTTP broadcast, trigger
/// regex, disk logging) can no longer eat the driver's receive buffer and drop
/// bytes. The queue is bounded with DropOldest, so a stuck consumer never
/// blocks the receive loop (and its evictions are reported), and Close drains
/// what was already read.
/// </summary>
public class SerialServiceRxDispatchTests
{
    [Fact]
    public async Task Dispatch_hands_off_without_running_the_subscriber_inline()
    {
        // If the subscriber ran inline on the caller, this dispatch could never
        // return while the subscriber is blocked — that is the whole point of
        // moving the chain off the SerialPort.DataReceived thread. (Comparing
        // managed thread ids is NOT used here: under full-suite load the pool
        // reuses the awaiting test's thread for the drain continuation.)
        using var svc = new SerialService();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = 0;

        svc.OnDataReceived += _ =>
        {
            Interlocked.Increment(ref observed);
            entered.TrySetResult();
            release.Task.Wait(TimeSpan.FromSeconds(5));
        };

        var dispatch = Task.Run(() => { svc.DispatchForTest(NewEntry(1)); return true; });
        Assert.True(await dispatch.WaitAsync(TimeSpan.FromSeconds(2)),
            "dispatch blocked on a stuck subscriber — the chain still runs inline");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        release.TrySetResult();
        Assert.Equal(1, observed);
    }

    [Fact]
    public async Task Close_drains_entries_already_dispatched()
    {
        using var svc = new SerialService();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = 0;
        Task? closeTask = null;
        // Sampled inside the subscriber callback: while the drain loop is
        // synchronously parked in that callback, a Close that honours the
        // drain contract cannot have returned yet. No wall-clock window is
        // involved — the old "not completed after a 200ms delay" assert
        // false-fired on coverage-instrumented CI, where the Delay
        // continuation itself only ran after Close's legitimate 2s drain
        // deadline had already expired.
        var closeReturnedDuringDispatch = false;

        svc.OnDataReceived += _ =>
        {
            // Block the drain loop on the first entry so the second one stays
            // queued — that is the state Close must wait out.
            if (Interlocked.Increment(ref observed) == 1)
            {
                entered.TrySetResult();
                // 15s, not 5s: under a saturated threadpool (full-suite
                // parallelism) a 5s window failed for scheduling reasons, not
                // because the drain misbehaved. Real regressions block forever,
                // so any finite window still catches them.
                release.Task.Wait(TimeSpan.FromSeconds(15));
            }
            else
            {
                closeReturnedDuringDispatch = closeTask?.IsCompleted ?? false;
            }
        };

        svc.DispatchForTest(NewEntry(1));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        svc.DispatchForTest(NewEntry(2));

        closeTask = Task.Run(() => svc.Close());
        // Release immediately: what this test locks is "Close returns only
        // after the queued entry was delivered", not how long Close must
        // stay blocked — betting on that with a timer is what broke above.
        release.TrySetResult();

        await closeTask.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.False(closeReturnedDuringDispatch,
            "Close returned while the queued entry was still being dispatched");
        Assert.Equal(2, observed);
    }

    [Fact]
    public async Task A_full_queue_drops_oldest_without_blocking_and_is_reported()
    {
        using var svc = new SerialService();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = 0;
        var errors = new List<string>();
        svc.OnError += errors.Add;

        svc.OnDataReceived += _ =>
        {
            if (Interlocked.Increment(ref observed) == 1)
            {
                entered.TrySetResult();
                release.Task.Wait(TimeSpan.FromSeconds(5));
            }
        };

        // Entry 1 is consumed and blocks the drain loop; the queue is now empty.
        svc.DispatchForTest(NewEntry(1));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Exactly one more than the 10_000 capacity: the last TryWrite must not
        // block, and the drop must be reported.
        for (int i = 2; i <= 10_002; i++)
            svc.DispatchForTest(NewEntry(i));

        Assert.Contains(errors, e => e.Contains("RX dispatch queue overflow"));

        release.TrySetResult();
        // Dispose → Close drains the queue; every surviving entry is delivered.
        svc.Dispose();
        Assert.Equal(10_001, observed);
    }

    private static LogEntry NewEntry(int id) => new()
    {
        Id = id,
        Timestamp = DateTime.Now,
        Direction = "RX",
        RawHex = "AA",
        Text = "AA"
    };
}
