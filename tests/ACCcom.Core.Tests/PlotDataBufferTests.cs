using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

/// <summary>
/// Tests for PlotDataBuffer — the incrementally-maintained min/max ring buffer
/// extracted from the WPF PlotViewModel (R32 optimization). Guards the extrema
/// logic that is otherwise only reachable through a UI host.
/// </summary>
public class PlotDataBufferTests
{
    [Fact]
    public void Add_FirstPoint_SeedsMinMax()
    {
        var buffer = new PlotDataBuffer();
        buffer.Add(42);

        Assert.Equal(1, buffer.Count);
        Assert.Equal(42, buffer.MinValue);
        Assert.Equal(42, buffer.MaxValue);
    }

    [Fact]
    public void Add_AscendingValues_TracksMax()
    {
        var buffer = new PlotDataBuffer();
        buffer.Add(1);
        buffer.Add(5);
        buffer.Add(3);

        Assert.Equal(1, buffer.MinValue);
        Assert.Equal(5, buffer.MaxValue);
        Assert.Equal(3, buffer.Count);
    }

    [Fact]
    public void Add_NewExtremes_UpdateIncrementally()
    {
        var buffer = new PlotDataBuffer();
        buffer.Add(10);
        buffer.Add(-5);
        buffer.Add(20);

        Assert.Equal(-5, buffer.MinValue);
        Assert.Equal(20, buffer.MaxValue);
    }

    [Fact]
    public void Add_ExceedsCapacity_EvictsOldestAndRescans()
    {
        // Capacity clamps to a minimum of 10.
        var buffer = new PlotDataBuffer(maxPoints: 10);
        buffer.Add(100); // will be evicted
        for (int i = 0; i < 9; i++) buffer.Add(50);
        buffer.Add(200);
        buffer.Add(1);   // exceeds capacity; 100 evicted

        Assert.Equal(10, buffer.Count);
        // 100 was the max but is gone; 200 is now max, 1 is min.
        Assert.Equal(1, buffer.MinValue);
        Assert.Equal(200, buffer.MaxValue);
    }

    [Fact]
    public void Add_EvictsMaxPoint_RecomputesCorrectly()
    {
        var buffer = new PlotDataBuffer(maxPoints: 10);
        buffer.Add(999); // max, will be evicted
        for (int i = 0; i < 9; i++) buffer.Add(5);
        buffer.Add(7);   // evicts 999

        Assert.Equal(10, buffer.Count);
        Assert.Equal(5, buffer.MinValue);
        Assert.Equal(7, buffer.MaxValue);
    }

    [Fact]
    public void Add_EvictsMinPoint_RecomputesCorrectly()
    {
        var buffer = new PlotDataBuffer(maxPoints: 10);
        buffer.Add(-999); // min, will be evicted
        for (int i = 0; i < 9; i++) buffer.Add(5);
        buffer.Add(7);    // evicts -999

        Assert.Equal(5, buffer.MinValue);
        Assert.Equal(7, buffer.MaxValue);
    }

    [Fact]
    public void Clear_ResetsEverything()
    {
        var buffer = new PlotDataBuffer();
        buffer.Add(1);
        buffer.Add(2);
        buffer.Clear();

        Assert.Equal(0, buffer.Count);
        Assert.Equal(0, buffer.MinValue);
        Assert.Equal(0, buffer.MaxValue);
        Assert.Empty(buffer.GetSnapshot());
    }

    [Fact]
    public void MaxPoints_SetLower_TrimsAndRescans()
    {
        var buffer = new PlotDataBuffer(maxPoints: 20);
        for (int i = 0; i < 20; i++) buffer.Add(i);
        Assert.Equal(20, buffer.Count);

        buffer.MaxPoints = 15;

        Assert.Equal(15, buffer.Count);
        // Values 5..19 remain.
        Assert.Equal(5, buffer.MinValue);
        Assert.Equal(19, buffer.MaxValue);
    }

    [Fact]
    public void MaxPoints_ClampedToMinimum()
    {
        var buffer = new PlotDataBuffer();
        buffer.MaxPoints = 1; // clamps to 10
        Assert.Equal(10, buffer.MaxPoints);
    }

    [Fact]
    public void GetSnapshot_ReturnsCopy()
    {
        var buffer = new PlotDataBuffer();
        buffer.Add(1);
        buffer.Add(2);

        var snapshot = buffer.GetSnapshot();
        Assert.Equal(2, snapshot.Count);

        // Mutating the returned list must not affect the buffer.
        ((List<double>)snapshot).Add(99);
        Assert.Equal(2, buffer.Count);
    }
}
