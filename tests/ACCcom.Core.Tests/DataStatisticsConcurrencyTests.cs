using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

/// <summary>
/// The two sampling rings in <see cref="DataStatistics"/> were rewritten from
/// Monitor-locked to lock-free producer-sequence rings: every RecordRx/RecordTx
/// used to take two locks, and the 1Hz rate readers walked up to 16384 slots
/// UNDER the lock — stalling every producer for the whole scan. Producers are
/// genuinely multiple (the serial drain task, the network bridge's receive
/// loop, one drain per multi-port session, TX senders on UI and background
/// threads), so each producer claims its own slot via an Interlocked sequence
/// instead of a single-writer head pointer.
/// </summary>
public class DataStatisticsConcurrencyTests
{
    [Fact]
    public void Concurrent_RecordRx_does_not_lose_totals()
    {
        var stats = new DataStatistics();
        const int writers = 4, perWriter = 5_000;

        Parallel.For(0, writers, _ =>
        {
            for (int i = 0; i < perWriter; i++) stats.RecordRx(1);
        });

        // The 16384-sample ring necessarily overflows, but the cumulative
        // counters are independent atomics and must be exact.
        Assert.Equal(writers * perWriter, stats.TotalRxFrames);
        Assert.Equal(writers * perWriter, stats.TotalRxBytes);
    }

    [Fact]
    public void Concurrent_mixed_recording_keeps_rings_and_rates_sane()
    {
        var stats = new DataStatistics();

        // RX from several threads (drain, network bridge, per-port drains) and
        // TX from others — the exact production topology.
        Parallel.Invoke(
            () => { for (int i = 0; i < 2_000; i++) stats.RecordRx(10); },
            () => { for (int i = 0; i < 2_000; i++) stats.RecordRx(20); },
            () => { for (int i = 0; i < 2_000; i++) stats.RecordRx(5); },
            () => { for (int i = 0; i < 2_000; i++) stats.RecordTx(8); },
            () => { for (int i = 0; i < 2_000; i++) stats.RecordTx(4); });

        Assert.Equal(6_000, stats.TotalRxFrames);
        Assert.Equal(70_000, stats.TotalRxBytes); // (10+20+5) per writer × 2000
        Assert.Equal(4_000, stats.TotalTxFrames);

        // Rates are computed from the rings through the lock-free path; they
        // must stay finite and non-negative (a torn read may transiently
        // undercount, never corrupt).
        Assert.True(stats.RxBytesPerSecond >= 0);
        Assert.True(stats.RxFramesPerSecond >= 0);
        Assert.True(stats.TxBytesPerSecond >= 0);
        Assert.True(stats.TxFramesPerSecond >= 0);
        Assert.True(stats.AvgFrameIntervalMs >= 0);
    }

    [Fact]
    public async Task Concurrent_readers_during_recording_do_not_throw()
    {
        var stats = new DataStatistics();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        var reader = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                // Exactly what the 1Hz status-bar tick does.
                _ = stats.RxBytesPerSecond;
                _ = stats.RxFramesPerSecond;
                _ = stats.AvgFrameIntervalMs;
                _ = stats.ErrorRate;
            }
        });

        Parallel.For(0, 4, _ =>
        {
            for (int i = 0; i < 5_000; i++)
            {
                stats.RecordRx(7);
                stats.RecordTx(3);
            }
        });

        await reader.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(20_000, stats.TotalRxFrames);
    }

    [Fact]
    public void Reset_during_recording_leaves_counters_usable()
    {
        var stats = new DataStatistics();
        for (int i = 0; i < 100; i++) stats.RecordRx(4);

        stats.Reset();

        Assert.Equal(0, stats.TotalRxFrames);
        Assert.Equal(0, stats.RxBytesPerSecond);
        Assert.Equal(0, stats.AvgFrameIntervalMs);

        // The rings must still accept samples after the sequence reset.
        for (int i = 0; i < 10; i++) stats.RecordRx(6);
        Assert.Equal(10, stats.TotalRxFrames);
        Assert.True(stats.AvgFrameIntervalMs >= 0);
    }
}
