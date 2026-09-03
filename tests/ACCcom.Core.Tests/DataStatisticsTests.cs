using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class DataStatisticsTests
{
    [Fact]
    public void RecordRx_UpdatesByteAndFrameCounts()
    {
        // Arrange
        var stats = new DataStatistics();

        // Act
        stats.RecordRx(10);
        stats.RecordRx(20);

        // Assert
        Assert.Equal(30, stats.TotalRxBytes);
        Assert.Equal(2, stats.TotalRxFrames);
    }

    [Fact]
    public void RxBytesPerSecond_ReturnsNonZeroAfterRecording()
    {
        // Arrange
        var stats = new DataStatistics();

        // Act
        stats.RecordRx(100);
        Thread.Sleep(50);
        stats.RecordRx(100);

        // Assert
        Assert.True(stats.RxBytesPerSecond > 0,
            $"Expected positive rate but got {stats.RxBytesPerSecond}");
    }

    [Fact]
    public void RxBytesPerSecond_ReturnsZeroWhenNoData()
    {
        // Arrange
        var stats = new DataStatistics();

        // Act & Assert
        Assert.Equal(0, stats.RxBytesPerSecond);
    }

    [Fact]
    public void RecordError_UpdatesErrorCount()
    {
        // Arrange
        var stats = new DataStatistics();

        // Act
        stats.RecordError();
        stats.RecordError();
        stats.RecordError();

        // Assert
        Assert.Equal(3, stats.TotalErrorFrames);
    }

    [Fact]
    public void ErrorRate_CalculatesCorrectly()
    {
        // Arrange
        var stats = new DataStatistics();

        // Act
        stats.RecordRx(10);
        stats.RecordRx(10);
        stats.RecordRx(10);
        stats.RecordRx(10);
        stats.RecordError();

        // Assert
        Assert.Equal(25.0, stats.ErrorRate);
    }

    [Fact]
    public void ErrorRate_ReturnsZeroWhenNoFrames()
    {
        // Arrange
        var stats = new DataStatistics();

        // Act & Assert
        Assert.Equal(0, stats.ErrorRate);
    }

    [Fact]
    public void Reset_ClearsAllStats()
    {
        // Arrange
        var stats = new DataStatistics();
        stats.RecordRx(50);
        stats.RecordRx(30);
        stats.RecordError();

        // Act
        stats.Reset();

        // Assert
        Assert.Equal(0, stats.TotalRxBytes);
        Assert.Equal(0, stats.TotalRxFrames);
        Assert.Equal(0, stats.TotalErrorFrames);
        Assert.Equal(0, stats.RxBytesPerSecond);
        Assert.Equal(0, stats.ErrorRate);
    }

    [Fact]
    public void AvgFrameIntervalMs_ReturnsZeroWhenNoData()
    {
        // Arrange
        var stats = new DataStatistics();

        // Act & Assert
        Assert.Equal(0, stats.AvgFrameIntervalMs);
    }

    [Fact]
    public void AvgFrameIntervalMs_CalculatesAfterMultipleFrames()
    {
        // Arrange
        var stats = new DataStatistics();

        // Act
        stats.RecordRx(1);
        Thread.Sleep(20);
        stats.RecordRx(1);
        Thread.Sleep(20);
        stats.RecordRx(1);

        // Assert
        Assert.True(stats.AvgFrameIntervalMs > 0,
            $"Expected positive interval but got {stats.AvgFrameIntervalMs}");
    }

    [Fact]
    public void RxFramesPerSecond_ReturnsZeroWhenNoData()
    {
        // Arrange
        var stats = new DataStatistics();

        // Act & Assert
        Assert.Equal(0, stats.RxFramesPerSecond);
    }

    [Fact]
    public void RxFramesPerSecond_ReturnsNonZeroAfterRecording()
    {
        // Arrange
        var stats = new DataStatistics();

        // Act
        stats.RecordRx(10);
        Thread.Sleep(50);
        stats.RecordRx(10);

        // Assert
        Assert.True(stats.RxFramesPerSecond > 0,
            $"Expected positive frame rate but got {stats.RxFramesPerSecond}");
    }

    // ========== RecordTx (mirrors RecordRx for outbound traffic) ==========

    [Fact]
    public void RecordTx_UpdatesByteAndFrameCounts()
    {
        var stats = new DataStatistics();
        stats.RecordTx(10);
        stats.RecordTx(20);

        Assert.Equal(30, stats.TotalTxBytes);
        Assert.Equal(2, stats.TotalTxFrames);
    }

    [Fact]
    public void RecordTx_DoesNotAffectRxCounters()
    {
        var stats = new DataStatistics();
        stats.RecordRx(100);
        stats.RecordTx(50);

        // RX stats untouched, TX independent
        Assert.Equal(100, stats.TotalRxBytes);
        Assert.Equal(1, stats.TotalRxFrames);
        Assert.Equal(50, stats.TotalTxBytes);
        Assert.Equal(1, stats.TotalTxFrames);
    }

    [Fact]
    public void TxBytesPerSecond_ZeroWhenNoData()
    {
        var stats = new DataStatistics();
        Assert.Equal(0, stats.TxBytesPerSecond);
        Assert.Equal(0, stats.TxFramesPerSecond);
    }

    [Fact]
    public void TxBytesPerSecond_IncreasesAfterRecentTraffic()
    {
        var stats = new DataStatistics();
        stats.RecordTx(100);
        Thread.Sleep(50);
        stats.RecordTx(100);

        Assert.True(stats.TxBytesPerSecond > 0,
            $"Expected positive TX rate but got {stats.TxBytesPerSecond}");
    }

    [Fact]
    public void Reset_ClearsTxSamplesAndCounters()
    {
        var stats = new DataStatistics();
        stats.RecordTx(64);
        stats.RecordTx(128);
        Assert.Equal(192, stats.TotalTxBytes);

        stats.Reset();

        Assert.Equal(0, stats.TotalTxBytes);
        Assert.Equal(0, stats.TotalTxFrames);
        Assert.Equal(0, stats.TxBytesPerSecond);
        // RX side should also be cleared (symmetric reset)
        Assert.Equal(0, stats.TotalRxBytes);
    }

    [Fact]
    public void HighFrequencyRecording_KeepsCountersAndRateAccurate()
    {
        // Ring storage must not drop accumulated totals at high frame rates.
        var stats = new DataStatistics();
        for (int i = 0; i < 100_000; i++)
            stats.RecordRx(4);

        Assert.Equal(400_000, stats.TotalRxBytes);
        Assert.Equal(100_000, stats.TotalRxFrames);
        // The 5s window covers all recent samples, so the instantaneous rate
        // should be a large positive number (roughly 400k bytes over elapsed time).
        Assert.True(stats.RxBytesPerSecond > 0,
            $"Expected positive rate but got {stats.RxBytesPerSecond}");
    }

    [Fact]
    public void Reset_AfterHighFrequency_ClearsRing()
    {
        var stats = new DataStatistics();
        for (int i = 0; i < 50_000; i++)
            stats.RecordRx(1);
        stats.Reset();

        Assert.Equal(0, stats.TotalRxBytes);
        Assert.Equal(0, stats.TotalRxFrames);
        Assert.Equal(0, stats.RxBytesPerSecond);
        Assert.Equal(0, stats.RxFramesPerSecond);
    }
}
