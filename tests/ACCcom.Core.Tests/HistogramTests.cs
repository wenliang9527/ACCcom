using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class HistogramTests
{
    [Fact]
    public void Record_SingleValue_CountAndSum()
    {
        var h = new Histogram();
        h.Record(42);

        Assert.Equal(1, h.Count);
        Assert.Equal(42, h.Sum);
    }

    [Fact]
    public void Record_MultipleValues_Accumulates()
    {
        var h = new Histogram();
        h.Record(1);
        h.Record(2);
        h.Record(3);

        Assert.Equal(3, h.Count);
        Assert.Equal(6, h.Sum);
    }

    [Fact]
    public void GetBuckets_Empty_AllZeroPlusInfinity()
    {
        var h = new Histogram();
        var buckets = h.GetBuckets();

        // 10 fixed bounds + the +Inf bucket.
        Assert.Equal(11, buckets.Count);
        Assert.All(buckets, b => Assert.Equal(0, b.Count));
        Assert.Equal(double.PositiveInfinity, buckets[^1].Le);
    }

    [Fact]
    public void Record_ValueInLowBucket_CountsPrefixBuckets()
    {
        var h = new Histogram();
        h.Record(0.5); // below the first bound (1)

        var buckets = h.GetBuckets();
        // Every bucket whose upper bound >= 0.5 includes this value (cumulative
        // Prometheus-style buckets), so all buckets count 1.
        Assert.All(buckets, b => Assert.Equal(1, b.Count));
        Assert.Equal(0.5, h.Sum);
    }

    [Fact]
    public void Record_ValueAtBoundary_LandsInNextBucket()
    {
        var h = new Histogram();
        h.Record(1); // exactly the first bound

        var buckets = h.GetBuckets();
        // Value == 1 belongs to the (1, 5] bucket, so the first bucket (le=1)
        // stays at 0 and all following count 1.
        Assert.Equal(0, buckets[0].Count);
        Assert.All(buckets.Skip(1), b => Assert.Equal(1, b.Count));
    }

    [Fact]
    public void Record_LargeValue_OnlyInfinityBucket()
    {
        var h = new Histogram();
        h.Record(50000); // beyond the largest bound (5000)

        var buckets = h.GetBuckets();
        // All bounded buckets stay 0; only +Inf counts it.
        Assert.All(buckets.Take(buckets.Count - 1), b => Assert.Equal(0, b.Count));
        Assert.Equal(1, buckets[^1].Count);
    }

    [Fact]
    public void Record_ZeroAndNegative_GoToLowestBucket()
    {
        var h = new Histogram();
        h.Record(0);
        h.Record(-3);

        Assert.Equal(2, h.Count);
        Assert.Equal(-3, h.Sum);
        Assert.All(h.GetBuckets(), b => Assert.Equal(2, b.Count));
    }

    [Fact]
    public async Task ParallelRecords_NoLostUpdates()
    {
        var h = new Histogram();
        const int threads = 8;
        const int perThread = 1000;

        var barrier = new Barrier(threads);
        var tasks = Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < perThread; i++) h.Record(1);
        })).ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(threads * perThread, h.Count);
        Assert.Equal(threads * perThread, h.Sum);
    }
}
