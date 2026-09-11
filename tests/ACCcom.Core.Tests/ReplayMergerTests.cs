using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class ReplayMergerTests
{
    private static LogEntry Entry(string dir, DateTime ts)
        => new() { Direction = dir, Timestamp = ts };

    [Fact]
    public void MergeByTimestamp_InterleavesByTime()
    {
        var baseTime = new DateTime(2026, 9, 11, 10, 0, 0);
        var rx = new List<LogEntry> { Entry("RX", baseTime.AddSeconds(1)), Entry("RX", baseTime.AddSeconds(3)) };
        var tx = new List<LogEntry> { Entry("TX", baseTime.AddSeconds(2)) };

        var merged = ReplayMerger.MergeByTimestamp(rx, tx);

        Assert.Equal(3, merged.Count);
        Assert.Equal("RX", merged[0].Direction);
        Assert.Equal("TX", merged[1].Direction);
        Assert.Equal("RX", merged[2].Direction);
    }

    [Fact]
    public void MergeByTimestamp_EqualTimestamps_KeepRxBeforeTx()
    {
        var t = new DateTime(2026, 9, 11, 10, 0, 0);
        var rx = new List<LogEntry> { Entry("RX", t) };
        var tx = new List<LogEntry> { Entry("TX", t) };

        var merged = ReplayMerger.MergeByTimestamp(rx, tx);

        Assert.Equal(new[] { "RX", "TX" }, merged.Select(e => e.Direction));
    }

    [Fact]
    public void MergeByTimestamp_DoesNotMutateInputs()
    {
        var t = new DateTime(2026, 9, 11, 10, 0, 0);
        var rx = new List<LogEntry> { Entry("RX", t.AddSeconds(3)) };
        var tx = new List<LogEntry> { Entry("TX", t.AddSeconds(1)) };

        ReplayMerger.MergeByTimestamp(rx, tx);

        Assert.Equal(new[] { "RX" }, rx.Select(e => e.Direction));
        Assert.Equal(new[] { "TX" }, tx.Select(e => e.Direction));
    }

    [Fact]
    public void MergeByTimestamp_NullOrEmptyLists_Handled()
    {
        var t = new DateTime(2026, 9, 11, 10, 0, 0);

        var onlyRx = ReplayMerger.MergeByTimestamp(new List<LogEntry> { Entry("RX", t) }, null!);
        Assert.Single(onlyRx);

        var onlyTx = ReplayMerger.MergeByTimestamp(null!, new List<LogEntry> { Entry("TX", t) });
        Assert.Single(onlyTx);

        Assert.Empty(ReplayMerger.MergeByTimestamp(null!, null!));
    }
}
