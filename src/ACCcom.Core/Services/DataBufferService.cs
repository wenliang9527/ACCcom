using System.Text.RegularExpressions;
using System.Threading.Channels;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class DataBufferService : IDisposable
{
    private readonly Channel<LogEntry> _channel;
    private readonly ChannelWriter<LogEntry> _writer;
    private readonly LogEntry?[] _ringBuffer;
    private int _head;
    private int _count;
    private readonly int _capacity;
    private readonly ReaderWriterLockSlim _rwLock = new();
    private readonly List<DataBufferWaiter> _waiters = new();
    private readonly object _waiterLock = new();

    public DataBufferService(int capacity = 10000)
    {
        _capacity = capacity;
        _ringBuffer = new LogEntry?[capacity];
        _channel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
        _writer = _channel.Writer;
    }

    private void RingAdd(LogEntry entry)
    {
        _ringBuffer[_head] = entry;
        _head = (_head + 1) % _capacity;
        if (_count < _capacity) _count++;
    }

    private List<LogEntry> RingSnapshot()
    {
        var list = new List<LogEntry>(_count);
        if (_count == 0) return list;
        var start = (_head - _count + _capacity) % _capacity;
        for (int i = 0; i < _count; i++)
        {
            var idx = (start + i) % _capacity;
            if (_ringBuffer[idx] is { } entry)
                list.Add(entry);
        }
        return list;
    }

    public void AddEntry(LogEntry entry)
    {
        _rwLock.EnterWriteLock();
        try { RingAdd(entry); }
        finally { _rwLock.ExitWriteLock(); }

        _writer.TryWrite(entry);

        lock (_waiterLock)
        {
            for (int i = _waiters.Count - 1; i >= 0; i--)
            {
                var waiter = _waiters[i];
                if (waiter.Completed)
                {
                    _waiters.RemoveAt(i);
                    continue;
                }
                if (waiter.Matches(entry))
                    waiter.Tcs.TrySetResult(entry);
            }
        }
    }

    public List<LogEntry> GetEntriesSince(int id)
    {
        _rwLock.EnterReadLock();
        try
        {
            if (_count == 0) return new List<LogEntry>();
            var result = new List<LogEntry>(_count);
            var start = (_head - _count + _capacity) % _capacity;
            for (int i = 0; i < _count; i++)
            {
                var idx = (start + i) % _capacity;
                var entry = _ringBuffer[idx];
                if (entry != null && entry.Id > id)
                    result.Add(entry);
            }
            return result;
        }
        finally { _rwLock.ExitReadLock(); }
    }

    public void Clear()
    {
        _rwLock.EnterWriteLock();
        try
        {
            Array.Clear(_ringBuffer);
            _count = 0;
            _head = 0;
        }
        finally { _rwLock.ExitWriteLock(); }
    }

    public void Clear(string? direction)
    {
        _rwLock.EnterWriteLock();
        try
        {
            if (string.IsNullOrEmpty(direction) ||
                direction.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                Array.Clear(_ringBuffer);
                _head = 0;
                _count = 0;
                return;
            }

            var clearRx = direction.Equals("rx", StringComparison.OrdinalIgnoreCase);
            var clearTx = direction.Equals("tx", StringComparison.OrdinalIgnoreCase);

            var snapshot = RingSnapshot();
            var keep = snapshot.Where(e =>
                (!clearRx && e.Direction == "RX") ||
                (!clearTx && e.Direction == "TX")).ToList();

            Array.Clear(_ringBuffer);
            _head = 0;
            _count = 0;
            foreach (var e in keep) RingAdd(e);
        }
        finally { _rwLock.ExitWriteLock(); }
    }

    public int Count()
    {
        _rwLock.EnterReadLock();
        try { return _count; }
        finally { _rwLock.ExitReadLock(); }
    }

    public void CancelWaiters()
    {
        List<DataBufferWaiter> snapshot;
        lock (_waiterLock) { snapshot = _waiters.ToList(); _waiters.Clear(); }
        foreach (var waiter in snapshot)
            waiter.Tcs.TrySetResult(null);
    }

    public int CountWhere(Func<LogEntry, bool> predicate)
    {
        _rwLock.EnterReadLock();
        try
        {
            int c = 0;
            var start = (_head - _count + _capacity) % _capacity;
            for (int i = 0; i < _count; i++)
            {
                var idx = (start + i) % _capacity;
                var entry = _ringBuffer[idx];
                if (entry != null && predicate(entry)) c++;
            }
            return c;
        }
        finally { _rwLock.ExitReadLock(); }
    }



    /// <summary>
    /// Wait for a buffer entry matching the given pattern and filters.
    /// Also checks existing buffer entries so data arriving before the wait is not missed.
    /// </summary>
    public Task<LogEntry?> WaitForMatchAsync(
        string pattern,
        string matchMode = "contains",
        bool matchHex = false,
        string? direction = null,
        int timeoutMs = 5000,
        CancellationToken ct = default)
    {
        var waiter = new DataBufferWaiter
        {
            Pattern = pattern,
            MatchMode = matchMode,
            MatchHex = matchHex,
            Direction = direction,
            Tcs = new TaskCompletionSource<LogEntry?>(TaskCreationOptions.RunContinuationsAsynchronously)
        };

        _rwLock.EnterReadLock();
        try
        {
            if (_count > 0)
            {
                var start = (_head - _count + _capacity) % _capacity;
                for (int i = 0; i < _count; i++)
                {
                    var idx = (start + i) % _capacity;
                    var entry = _ringBuffer[idx];
                    if (entry != null && waiter.Matches(entry))
                    {
                        waiter.Tcs.TrySetResult(entry);
                        return waiter.Tcs.Task;
                    }
                }
            }
        }
        finally { _rwLock.ExitReadLock(); }

        lock (_waiterLock) { _waiters.Add(waiter); }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delayTask = Task.Delay(timeoutMs, cts.Token);
        return WaitForMatchInternal(waiter, delayTask, cts);
    }

    private static async Task<LogEntry?> WaitForMatchInternal(DataBufferWaiter waiter, Task delayTask, CancellationTokenSource cts)
    {
        try
        {
            await Task.WhenAny(waiter.Tcs.Task, delayTask).ConfigureAwait(false);
        }
        finally
        {
            await cts.CancelAsync().ConfigureAwait(false);
            cts.Dispose();
        }

        if (waiter.Tcs.Task.IsCompletedSuccessfully)
            return await waiter.Tcs.Task.ConfigureAwait(false);

        waiter.Tcs.TrySetResult(null);
        return null;
    }

    public void Dispose()
    {
        _writer.TryComplete();
        CancelWaiters();
        _rwLock.Dispose();
    }
}

public class DataBufferWaiter
{
    public string Pattern { get; set; } = "";
    public string MatchMode { get; set; } = "contains";
    public bool MatchHex { get; set; }
    public string? Direction { get; set; }
    public TaskCompletionSource<LogEntry?> Tcs { get; set; } = new();
    public bool Completed => Tcs.Task.IsCompleted;

    public bool Matches(LogEntry entry)
    {
        return PatternMatcher.Matches(entry, Pattern, MatchMode ?? "contains", MatchHex, Direction);
    }
}
