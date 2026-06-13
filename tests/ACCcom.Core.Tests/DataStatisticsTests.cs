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
}
