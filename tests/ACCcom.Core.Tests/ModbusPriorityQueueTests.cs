using ACCcom.Core.Services;
using ACCcom.Core.Models;

namespace ACCcom.Core.Tests;

public class ModbusPriorityQueueTests
{
    [Fact]
    public void HighPriority_CompletesFirst()
    {
        var q = new ModbusPriorityQueue<int>(maxLowPerSecond: 10);
        q.TryEnqueue(1, ModbusPriority.Low);
        q.TryEnqueue(2, ModbusPriority.High);
        Assert.Equal(2, q.TryDequeue());
        Assert.Equal(1, q.TryDequeue());
    }

    [Fact]
    public void RateLimit_BlocksLowPriority()
    {
        var q = new ModbusPriorityQueue<int>(maxLowPerSecond: 2);
        Assert.True(q.TryEnqueue(1, ModbusPriority.Low));
        Assert.True(q.TryEnqueue(2, ModbusPriority.Low));
        Assert.False(q.TryEnqueue(3, ModbusPriority.Low));
        Assert.True(q.TryEnqueue(4, ModbusPriority.High));
    }

    [Fact]
    public void Empty_ReturnsDefault()
    {
        var q = new ModbusPriorityQueue<int>(maxLowPerSecond: 10);
        Assert.Equal(0, q.TryDequeue());
    }

    [Fact]
    public void Count_TracksTotal()
    {
        var q = new ModbusPriorityQueue<int>(maxLowPerSecond: 10);
        q.TryEnqueue(1, ModbusPriority.Low);
        q.TryEnqueue(2, ModbusPriority.High);
        Assert.Equal(2, q.Count);
    }
}
