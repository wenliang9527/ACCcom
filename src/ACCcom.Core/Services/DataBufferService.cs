using System.Text.RegularExpressions;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class DataBufferService : IDisposable
{
    private readonly LogEntry?[] _ringBuffer;
    private int _head;
    private int _count;
    private int _maxId;
    private int _rxCount;
    private int _txCount;
    private int _waiterCount;
    private readonly int _capacity;
    private readonly object _lock = new();
    private readonly List<DataBufferWaiter> _waiters = new();
    private readonly object _waiterLock = new();
    private readonly MetricsCollector _metrics = MetricsCollector.Instance;
    private static readonly List<LogEntry> EmptyBuffer = new(0);

    public DataBufferService(int capacity = 10000)
    {
        _capacity = capacity;
        _ringBuffer = new LogEntry?[capacity];
    }

    private void RingAdd(LogEntry entry)
    {
        if (_count >= _capacity)
        {
            _metrics.RecordBufferOverrun();
            var evicted = _ringBuffer[_head];
            if (evicted != null)
                AdjustDirectionCount(evicted.Direction, -1);
        }

        _ringBuffer[_head] = entry;
        _head = (_head + 1) % _capacity;
        if (_count < _capacity) _count++;
        if (entry.Id > _maxId) _maxId = entry.Id;
        AdjustDirectionCount(entry.Direction, +1);

        _metrics.SetBufferUsage((double)_count / _capacity);
    }

    private void AdjustDirectionCount(string? direction, int delta)
    {
        if (direction == "RX") _rxCount += delta;
        else if (direction == "TX") _txCount += delta;
    }

    private List<LogEntry> RingSnapshot()
    {
        if (_count == 0) return new List<LogEntry>();
        var list = new List<LogEntry>(_count);
        var start = (_head - _count + _capacity) % _capacity;
        for (int i = 0; i < _count; i++)
        {
            var idx = (start + i) % _capacity;
            var entry = _ringBuffer[idx];
            if (entry != null) list.Add(entry);
        }
        return list;
    }

    public void AddEntry(LogEntry entry)
    {
        lock (_lock) { RingAdd(entry); }

        // Fast path: no active waiters, skip the waiter lock + scan entirely.
        if (Volatile.Read(ref _waiterCount) == 0)
            return;

        lock (_waiterLock)
        {
            for (int i = _waiters.Count - 1; i >= 0; i--)
            {
                var waiter = _waiters[i];
                if (waiter.Completed)
                {
                    _waiters.RemoveAt(i);
                    Interlocked.Decrement(ref _waiterCount);
                    continue;
                }
                if (waiter.Matches(entry))
                    waiter.Tcs.TrySetResult(entry);
            }
        }
    }

    /// <summary>Returns entries with Id > id (arrival order). Entries are stored
    /// in arrival order with strictly monotonic Ids (Interlocked.Increment in the
    /// receive paths), so the matching entries form a contiguous tail of the ring —
    /// a binary search locates the tail start in O(log N) instead of scanning all
    /// 10k slots per poll. Direction and limit are folded into the single tail
    /// copy.</summary>
    public List<LogEntry> GetEntriesSince(int id, string? direction = null, int limit = 0)
    {
        lock (_lock)
        {
            if (_count == 0 || id >= _maxId) return EmptyBuffer;

            var start = (_head - _count + _capacity) % _capacity;

            // Lower-bound search for the first linear index whose Id > id.
            int lo = 0, hi = _count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                var entry = _ringBuffer[(start + mid) % _capacity];
                if (entry != null && entry.Id > id)
                    hi = mid;
                else
                    lo = mid + 1;
            }

            // Pre-size modestly; most polls return a small tail of new entries.
            var result = new List<LogEntry>(Math.Min(_count - lo, limit > 0 ? limit : 64));
            for (int i = lo; i < _count; i++)
            {
                var entry = _ringBuffer[(start + i) % _capacity];
                if (entry == null) continue;
                if (direction != null && !string.Equals(entry.Direction, direction, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(entry);
                if (limit > 0 && result.Count >= limit)
                    break;
            }
            return result;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_ringBuffer);
            _count = 0;
            _head = 0;
            _maxId = 0;
            _rxCount = 0;
            _txCount = 0;
        }
        _metrics.SetBufferUsage(0);
    }

    public void Clear(string? direction)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(direction) ||
                direction.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                Array.Clear(_ringBuffer);
                _head = 0;
                _count = 0;
                _maxId = 0;
                _rxCount = 0;
                _txCount = 0;
                _metrics.SetBufferUsage(0);
                return;
            }

            var clearRx = direction.Equals("rx", StringComparison.OrdinalIgnoreCase);
            var clearTx = direction.Equals("tx", StringComparison.OrdinalIgnoreCase);

            var snapshot = RingSnapshot();
            var keep = new List<LogEntry>(snapshot.Count);
            foreach (var e in snapshot)
            {
                if ((!clearRx && e.Direction == "RX") ||
                    (!clearTx && e.Direction == "TX"))
                    keep.Add(e);
            }

            Array.Clear(_ringBuffer);
            _head = 0;
            _count = 0;
            _maxId = 0;
            _rxCount = 0;
            _txCount = 0;
            foreach (var e in keep) RingAdd(e);
        }
    }

    public int Count()
    {
        lock (_lock) { return _count; }
    }

    /// <summary>O(1) count of entries with the given direction ("RX"/"TX"), maintained
    /// incrementally in <see cref="RingAdd"/> instead of a full ring scan per poll.</summary>
    public int CountDirection(string direction)
    {
        lock (_lock)
        {
            if (direction == "RX") return _rxCount;
            if (direction == "TX") return _txCount;
            return 0;
        }
    }

    public void CancelWaiters()
    {
        List<DataBufferWaiter> snapshot;
        lock (_waiterLock)
        {
            snapshot = _waiters.ToList();
            _waiters.Clear();
            Volatile.Write(ref _waiterCount, 0);
        }
        foreach (var waiter in snapshot)
            waiter.Tcs.TrySetResult(null);
    }



    public int CountWhere(Func<LogEntry, bool> predicate)
    {
        lock (_lock)
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

        lock (_lock)
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

            // Register inside the same _lock critical section as the scan so there is
            // no gap between "no match found" and "waiter registered": an entry added
            // after this block sees the registered waiter (via _waiterCount) and is
            // delivered through AddEntry; one added before is found by the scan above.
            lock (_waiterLock)
            {
                _waiters.Add(waiter);
                Interlocked.Increment(ref _waiterCount);
            }
        }

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
        CancelWaiters();
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
