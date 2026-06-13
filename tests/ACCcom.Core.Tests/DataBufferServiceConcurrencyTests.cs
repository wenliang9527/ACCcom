using System.Threading;
using System.Threading.Tasks;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class DataBufferServiceConcurrencyTests
{
    private static LogEntry MakeEntry(int id, string direction = "RX", string text = "data", string hex = "44 41 54 41")
    {
        return new LogEntry
        {
            Id = id,
            Timestamp = DateTime.Now,
            Direction = direction,
            PortTag = "COM1",
            RawHex = hex,
            Text = text
        };
    }

    [Fact]
    public void TestConcurrentReadWrite()
    {
        var sut = new DataBufferService();
        var barrier = new ManualResetEventSlim(false);
        int readErrors = 0;
        int writeCount = 500;
        int readers = 4;
        var readerTasks = new Task[readers];

        for (int r = 0; r < readers; r++)
        {
            readerTasks[r] = Task.Run(() =>
            {
                barrier.Wait();
                for (int i = 0; i < writeCount; i++)
                {
                    try
                    {
                        var _ = sut.Count();
                        var __ = sut.GetEntriesSince(0);
                    }
                    catch
                    {
                        Interlocked.Increment(ref readErrors);
                    }
                }
            });
        }

        var writerTask = Task.Run(() =>
        {
            barrier.Wait();
            for (int i = 0; i < writeCount; i++)
            {
                sut.AddEntry(MakeEntry(i));
            }
        });

        barrier.Set();
        Task.WaitAll(readerTasks.Concat(new[] { writerTask }).ToArray());

        Assert.Equal(0, readErrors);
        Assert.Equal(writeCount, sut.Count());
    }

    [Fact]
    public async Task TestConcurrentReaders_WaitForMatch()
    {
        var sut = new DataBufferService();
        int matchCount = 0;

        var tasks = new Task[10];
        for (int i = 0; i < 10; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                var result = await sut.WaitForMatchAsync("target", timeoutMs: 2000);
                if (result != null) Interlocked.Increment(ref matchCount);
            });
        }

        await Task.Delay(50);
        sut.AddEntry(MakeEntry(1, text: "target"));

        await Task.WhenAll(tasks);

        Assert.Equal(10, matchCount);
    }

    [Fact]
    public async Task TestWaitForEntry()
    {
        var sut = new DataBufferService();

        _ = Task.Run(async () =>
        {
            await Task.Delay(30);
            sut.AddEntry(MakeEntry(1, text: "trigger_data"));
        });

        var result = await sut.WaitForMatchAsync("trigger_data", timeoutMs: 1000);

        Assert.NotNull(result);
        Assert.Equal("trigger_data", result!.Text);
    }

    [Fact]
    public async Task TestWaitForEntryTimeout()
    {
        var sut = new DataBufferService();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await sut.WaitForMatchAsync("never", timeoutMs: 200);
        sw.Stop();

        Assert.Null(result);
        Assert.True(sw.ElapsedMilliseconds >= 150);
    }

    [Fact]
    public async Task TestCancelWaiters()
    {
        var sut = new DataBufferService();

        var tasks = new Task<LogEntry?>[5];
        for (int i = 0; i < 5; i++)
        {
            tasks[i] = sut.WaitForMatchAsync("never_comes", timeoutMs: 5000);
        }

        await Task.Delay(50);
        sut.CancelWaiters();

        var results = await Task.WhenAll(tasks);

        foreach (var r in results)
        {
            Assert.Null(r);
        }
    }

    [Fact]
    public async Task TestCancelWaiters_FreesBlockedTasks()
    {
        var sut = new DataBufferService();

        var task = sut.WaitForMatchAsync("waiting", timeoutMs: 10000);

        await Task.Delay(30);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        sut.CancelWaiters();
        await task;
        sw.Stop();

        Assert.Null(task.Result);
        Assert.True(sw.ElapsedMilliseconds < 1000);
    }

    [Fact]
    public async Task TestConcurrentAddEntries_NoDataLoss()
    {
        var sut = new DataBufferService();
        int entryCount = 1000;
        var tasks = new Task[10];

        for (int t = 0; t < 10; t++)
        {
            int batch = t;
            tasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < entryCount; i++)
                {
                    sut.AddEntry(MakeEntry(batch * entryCount + i));
                }
            });
        }

        await Task.WhenAll(tasks);

        Assert.Equal(10 * entryCount, sut.Count());
    }

    [Fact]
    public async Task TestRingBufferCapacity()
    {
        var sut = new DataBufferService(capacity: 100);

        for (int i = 0; i < 200; i++)
        {
            sut.AddEntry(MakeEntry(i));
        }

        Assert.Equal(100, sut.Count());
        var entries = sut.GetEntriesSince(100);
        Assert.All(entries, e => Assert.True(e.Id >= 100));
    }
}
