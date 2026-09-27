using System.Text.RegularExpressions;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class DataBufferService : IDisposable
{
    private readonly LogEntry?[] _ringBuffer;
    /// <summary>Arrival sequence per ring slot. Entry.Id is NOT usable as a
    /// cursor: ids come from independent per-direction/per-port counters (RX
    /// #500 and TX #3 interleave as 500,1,501,2...), so "Id > since" silently
    /// skipped any entry whose counter lagged the cursor. The buffer-local
    /// sequence is strictly monotonic in RingAdd (= arrival) order, which is
    /// exactly what the binary search and the polling cursor need.</summary>
    private readonly int[] _seqBuffer;
    private int _nextSeq;
    private int _head;
    private int _count;
    private int _rxCount;
    private int _txCount;
    private int _waiterCount;
    private readonly int _capacity;
    private readonly object _lock = new();
    private readonly List<DataBufferWaiter> _waiters = new();
    private readonly object _waiterLock = new();
    /// <summary>One-shot arrival signal for long-poll readers
    /// (<see cref="WaitEntriesSinceAsync"/>): completed and cleared by
    /// <see cref="RingAdd(LogEntry)"/> under _lock, created by the reader under
    /// _lock — the same no-gap pattern as waiter registration. Guarded by _lock.</summary>
    private TaskCompletionSource<bool>? _arrivalTcs;
    private readonly MetricsCollector _metrics = MetricsCollector.Instance;

    public DataBufferService(int capacity = 10000)
    {
        // A non-positive capacity would divide by zero on the ring wrap
        // (_head % _capacity) or throw from the array allocation. Clamp to a
        // minimum so the buffer always has room for at least one entry.
        _capacity = Math.Max(1, capacity);
        _ringBuffer = new LogEntry?[_capacity];
        _seqBuffer = new int[_capacity];
    }

    private void RingAdd(LogEntry entry)
    {
        RingAdd(entry, ++_nextSeq);

        // Long-poll wake-up: a reader is parked in WaitEntriesSinceAsync until
        // something newer than its cursor arrives. TrySetResult with
        // RunContinuationsAsynchronously never runs the reader inline under
        // _lock, so releasing here is safe.
        var arrival = _arrivalTcs;
        if (arrival != null)
        {
            _arrivalTcs = null;
            arrival.TrySetResult(true);
        }
    }

    /// <summary>Insert at the head. <paramref name="seq"/> is a fresh ++_nextSeq
    /// for new entries, or the entry's previous sequence when a direction-clear
    /// rebuild re-inserts survivors (keeping their sequence prevents the polling
    /// cursor from receiving them again as duplicates).</summary>
    private void RingAdd(LogEntry entry, int seq)
    {
        if (_count >= _capacity)
        {
            _metrics.RecordBufferOverrun();
            var evicted = _ringBuffer[_head];
            if (evicted != null)
                AdjustDirectionCount(evicted.Direction, -1);
        }

        _ringBuffer[_head] = entry;
        _seqBuffer[_head] = seq;
        _head = (_head + 1) % _capacity;
        if (_count < _capacity) _count++;
        AdjustDirectionCount(entry.Direction, +1);

        _metrics.SetBufferUsage((double)_count / _capacity);
    }

    private void AdjustDirectionCount(string? direction, int delta)
    {
        if (direction == "RX") _rxCount += delta;
        else if (direction == "TX") _txCount += delta;
    }

    private List<(LogEntry Entry, int Seq)> RingSnapshot()
    {
        if (_count == 0) return new List<(LogEntry, int)>();
        var list = new List<(LogEntry, int)>(_count);
        var start = (_head - _count + _capacity) % _capacity;
        for (int i = 0; i < _count; i++)
        {
            var idx = (start + i) % _capacity;
            var entry = _ringBuffer[idx];
            if (entry != null) list.Add((entry, _seqBuffer[idx]));
        }
        return list;
    }

    public void AddEntry(LogEntry? entry)
    {
        if (entry == null) return;

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

    /// <summary>Returns entries whose buffer-local arrival sequence is greater
    /// than <paramref name="id"/> (arrival order). The cursor is an opaque
    /// sequence — echo back the <c>scannedMaxId</c> of the previous poll.
    /// Entry.Id cannot serve this role: ids come from independent
    /// per-direction/per-port counters, so they interleave non-monotonically in
    /// arrival order and "Id > since" silently skipped any entry whose counter
    /// lagged the cursor. Sequences are strictly increasing in RingAdd order, so
    /// the matching entries form a contiguous tail of the ring — a binary search
    /// locates the tail start in O(log N) instead of scanning all 10k slots per
    /// poll. Direction and limit are folded into the single tail copy.</summary>
    public List<LogEntry> GetEntriesSince(int id, string? direction = null, int limit = 0)
        => GetEntriesSince(id, direction, limit, out _);

    /// <summary>Same as <see cref="GetEntriesSince(int,string?,int)"/> but also
    /// reports the next polling cursor. It is the max sequence actually consumed
    /// by this call: the last returned entry's sequence, or — when the direction
    /// filter skipped a tail of higher-sequence entries and nothing was returned
    /// — the highest sequence scanned, so the caller's next poll never rescans
    /// skipped entries nor rewinds onto previously-returned ones.</summary>
    public List<LogEntry> GetEntriesSince(int id, string? direction, int limit, out int scannedMaxId)
    {
        lock (_lock)
        {
            scannedMaxId = id;
            if (_count == 0 || id >= _nextSeq) return new List<LogEntry>();

            var start = (_head - _count + _capacity) % _capacity;

            // Lower-bound search for the first linear index whose sequence > id.
            int lo = 0, hi = _count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_seqBuffer[(start + mid) % _capacity] > id)
                    hi = mid;
                else
                    lo = mid + 1;
            }

            // Pre-size modestly; most polls return a small tail of new entries.
            var result = new List<LogEntry>(Math.Min(_count - lo, limit > 0 ? limit : 64));
            int scanMax = id;
            int returnedMax = id;
            bool truncated = false;
            for (int i = lo; i < _count; i++)
            {
                var idx = (start + i) % _capacity;
                var entry = _ringBuffer[idx];
                if (entry == null) continue;
                var seq = _seqBuffer[idx];
                if (seq > scanMax) scanMax = seq;
                if (direction != null && !string.Equals(entry.Direction, direction, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(entry);
                if (seq > returnedMax) returnedMax = seq;
                if (limit > 0 && result.Count >= limit)
                {
                    truncated = true;
                    break;
                }
            }

            // When the limit cut the tail, the entries beyond it are still
            // unread — advance only to what was actually returned. When nothing
            // was returned (direction filter skipped the whole tail) advance to
            // the scan max so the next poll does not rescan those skipped
            // entries. Otherwise advance to the returned max.
            scannedMaxId = truncated ? returnedMax : scanMax;
            return result;
        }
    }

    /// <summary>
    /// Long-poll variant of <see cref="GetEntriesSince(int,string?,int,out int)"/>:
    /// returns immediately when the cursor already has data (or
    /// <paramref name="waitMs"/> is non-positive), otherwise parks until an
    /// entry newer than <paramref name="id"/> arrives or the wait times out,
    /// then performs the final read. Wake-up is event-driven (arrival signal
    /// released inside RingAdd under _lock — no polling loop, no 25ms timer
    /// granularity), so round-trips for agent cursor polls collapse from
    /// "every N ms" to "when data actually arrives".
    /// The registration/re-check runs under _lock with no gap: an entry added
    /// before registration is seen by the pre-registration seq check, one
    /// after is released by the signal.
    /// </summary>
    public async Task<(List<LogEntry> Entries, int ScannedMaxId)> WaitEntriesSinceAsync(
        int id, string? direction, int limit, int waitMs)
    {
        var entries = GetEntriesSince(id, direction, limit, out var maxId);
        if (entries.Count > 0 || maxId > id || waitMs <= 0)
            return (entries, maxId);

        var deadline = Environment.TickCount64 + waitMs;
        while (true)
        {
            Task signal;
            lock (_lock)
            {
                // Data newer than the cursor appeared between the read above
                // and registration — skip the wait entirely (no lost wakeup).
                // New data exists iff id < _nextSeq: RingAdd assigns
                // ++_nextSeq as the fresh seq, so the last assigned seq IS
                // _nextSeq (not _nextSeq - 1).
                if (_nextSeq > id) break;
                _arrivalTcs ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                signal = _arrivalTcs.Task;
            }

            var remaining = deadline - Environment.TickCount64;
            if (remaining <= 0) break;

            var delay = Task.Delay((int)Math.Min(remaining, int.MaxValue));
            var completed = await Task.WhenAny(signal, delay).ConfigureAwait(false);
            if (completed != signal) break; // timeout — fall through to the final read
        }

        return (GetEntriesSince(id, direction, limit, out var finalMax), finalMax);
    }

    /// <summary>Returns the newest <paramref name="tail"/> entries in arrival
    /// order without requiring a cursor — the "what just happened" read path
    /// (agents polling a device stream want the latest frames, not everything
    /// since cursor 0). <paramref name="scannedMaxId"/> reports the arrival
    /// sequence of the newest entry, so the caller can continue polling with
    /// <see cref="GetEntriesSince(int,string?,int)"/> from this cursor without
    /// re-reading or skipping anything.</summary>
    public List<LogEntry> GetTailEntries(int tail, string? direction, out int scannedMaxId)
    {
        lock (_lock)
        {
            if (_count == 0)
            {
                scannedMaxId = 0;
                return new List<LogEntry>();
            }

            var start = (_head - _count + _capacity) % _capacity;
            scannedMaxId = _seqBuffer[(_head - 1 + _capacity) % _capacity];
            if (tail <= 0) return new List<LogEntry>();

            var result = new List<LogEntry>(Math.Min(tail, _count));
            // Walk newest → oldest, keeping the first `tail` direction matches,
            // then reverse so callers see arrival order (oldest first).
            for (int i = _count - 1; i >= 0 && result.Count < tail; i--)
            {
                var entry = _ringBuffer[(start + i) % _capacity];
                if (entry == null) continue;
                if (direction != null && !string.Equals(entry.Direction, direction, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(entry);
            }
            result.Reverse();
            return result;
        }
    }

    /// <summary>Newest arrival sequence in cursor space (0 before any entry;
    /// sequence values start at 1). RingAdd assigns ++_nextSeq as the fresh
    /// seq, so the last assigned seq IS _nextSeq — not _nextSeq - 1 (that
    /// off-by-one was invisible to the change-detection wait, which only cares
    /// about monotonicity, but returned a wrong cursor to latestId consumers).
    /// Used for change-detection waits without copying entries, and by MCP
    /// wait-style tools to hand agents a continuation cursor (latestId) so a
    /// wait result can be followed by read_data(sinceId=latestId) without
    /// re-reading anything.</summary>
    public int LastSeq
    {
        get { lock (_lock) { return _nextSeq; } }
    }

    /// <summary>
    /// Waits until the buffer has seen no new entry for <paramref name="quietMs"/>
    /// consecutive milliseconds — i.e. the device finished streaming. Polls the
    /// arrival-sequence cursor (no entry copy) every <paramref name="pollMs"/>;
    /// any new entry (any direction) resets the quiet window. Returns true when
    /// the quiet window was reached, false when <paramref name="timeoutMs"/>
    /// elapsed first. Timestamps are sampled at each poll, so quiet is detected
    /// with up to <paramref name="pollMs"/> granularity.
    /// </summary>
    public async Task<bool> WaitForQuietAsync(int quietMs, int timeoutMs, int pollMs = 25, CancellationToken ct = default)
    {
        quietMs = Math.Max(1, quietMs);
        pollMs = Math.Max(1, pollMs);
        var deadline = Environment.TickCount64 + Math.Max(0, timeoutMs);
        var lastSeq = LastSeq;
        var quietSince = Environment.TickCount64;

        while (true)
        {
            await Task.Delay(pollMs, ct).ConfigureAwait(false);
            var now = Environment.TickCount64;

            var seq = LastSeq;
            if (seq != lastSeq)
            {
                lastSeq = seq;
                quietSince = now;
            }
            else if (now - quietSince >= quietMs)
            {
                return true;
            }

            if (now >= deadline) return false;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_ringBuffer);
            _count = 0;
            _head = 0;
            // _nextSeq deliberately survives: a cursor taken before the clear
            // must still match entries added after it (restarting at 1 would
            // hide them from that cursor until the sequence caught up).
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
                _rxCount = 0;
                _txCount = 0;
                _metrics.SetBufferUsage(0);
                return;
            }

            var clearRx = direction.Equals("rx", StringComparison.OrdinalIgnoreCase);
            var clearTx = direction.Equals("tx", StringComparison.OrdinalIgnoreCase);

            var snapshot = RingSnapshot();
            var keep = new List<(LogEntry, int)>(snapshot.Count);
            foreach (var (e, seq) in snapshot)
            {
                if ((!clearRx && e.Direction == "RX") ||
                    (!clearTx && e.Direction == "TX"))
                    keep.Add((e, seq));
            }

            Array.Clear(_ringBuffer);
            _head = 0;
            _count = 0;
            _rxCount = 0;
            _txCount = 0;
            // Survivors keep their original sequence: re-numbering would push
            // them past existing cursors and re-deliver them as duplicates.
            foreach (var (e, seq) in keep) RingAdd(e, seq);
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
        ArgumentNullException.ThrowIfNull(predicate);

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
    ///
    /// Locking contract: the snapshot and the waiter registration happen in one
    /// _lock critical section (no gap: entries added after release are
    /// delivered through AddEntry, entries before are in the snapshot), but the
    /// pattern scan runs OUTSIDE _lock — a regex over a full 10k ring could
    /// otherwise stall AddEntry/GetEntriesSince (and the serial RX thread behind
    /// them) for the whole scan. Immediate matches unregister before returning;
    /// timeout/completion unregisters in WaitForMatchInternal, so waiters never
    /// linger in the list.
    /// </summary>
    public Task<LogEntry?> WaitForMatchAsync(
        string pattern,
        string matchMode = "contains",
        bool matchHex = false,
        string? direction = null,
        int timeoutMs = 5000,
        CancellationToken ct = default)
    {
        // A null pattern would otherwise throw deep inside MatchesPattern's
        // contains branch (target.Contains(null)) — after the waiter is
        // registered, surfacing as a hard-to-trace async exception. Reject it
        // at the entry point.
        ArgumentNullException.ThrowIfNull(pattern);
        var waiter = new DataBufferWaiter
        {
            Pattern = pattern,
            MatchMode = matchMode,
            MatchHex = matchHex,
            Direction = direction,
            Tcs = new TaskCompletionSource<LogEntry?>(TaskCreationOptions.RunContinuationsAsynchronously)
        };

        List<LogEntry> snapshot;
        lock (_lock)
        {
            snapshot = SnapshotEntriesLocked();

            // Register inside the same _lock critical section as the snapshot so there is
            // no gap between "no match found" and "waiter registered": an entry added
            // after this block sees the registered waiter (via _waiterCount) and is
            // delivered through AddEntry; one added before is found by the snapshot scan.
            lock (_waiterLock)
            {
                _waiters.Add(waiter);
                Interlocked.Increment(ref _waiterCount);
            }
        }

        // Scan outside _lock (see method docs). TrySetResult is idempotent, so
        // racing with a concurrent AddEntry delivery is safe either way.
        for (int i = 0; i < snapshot.Count; i++)
        {
            var entry = snapshot[i];
            if (waiter.Matches(entry))
            {
                RemoveWaiter(waiter);
                waiter.Tcs.TrySetResult(entry);
                return waiter.Tcs.Task;
            }
        }

        // An entry arrived during the scan and already satisfied the waiter.
        if (waiter.Tcs.Task.IsCompleted)
            return waiter.Tcs.Task;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Task.Delay throws for negative timeouts; clamp so a non-positive
        // timeout behaves as "no wait" (immediate timeout path) instead of
        // surfacing an ArgumentOutOfRangeException to the caller.
        var delayMs = Math.Max(1, timeoutMs);
        var delayTask = Task.Delay(delayMs, cts.Token);
        return WaitForMatchInternal(waiter, delayTask, cts);
    }

    /// <summary>Unlists a finished waiter (immediate match, delivered match, or
    /// timeout). No-op if another path already removed it, so the waiter-count
    /// decrement stays balanced.</summary>
    private void RemoveWaiter(DataBufferWaiter waiter)
    {
        lock (_waiterLock)
        {
            if (_waiters.Remove(waiter))
                Interlocked.Decrement(ref _waiterCount);
        }
    }

    private async Task<LogEntry?> WaitForMatchInternal(DataBufferWaiter waiter, Task delayTask, CancellationTokenSource cts)
    {
        try
        {
            await Task.WhenAny(waiter.Tcs.Task, delayTask).ConfigureAwait(false);
        }
        finally
        {
            await cts.CancelAsync().ConfigureAwait(false);
            cts.Dispose();
            // Always unlist — a timed-out waiter used to linger until the next
            // AddEntry sweep, accumulating across polls with no traffic.
            RemoveWaiter(waiter);
        }

        if (waiter.Tcs.Task.IsCompletedSuccessfully)
            return await waiter.Tcs.Task.ConfigureAwait(false);

        waiter.Tcs.TrySetResult(null);
        return null;
    }

    /// <summary>Copies the live entries (references only, no tuples) for a
    /// scan that must run outside _lock. Caller holds _lock.</summary>
    private List<LogEntry> SnapshotEntriesLocked()
    {
        if (_count == 0) return new List<LogEntry>();
        var list = new List<LogEntry>(_count);
        var start = (_head - _count + _capacity) % _capacity;
        for (int i = 0; i < _count; i++)
        {
            var entry = _ringBuffer[(start + i) % _capacity];
            if (entry != null) list.Add(entry);
        }
        return list;
    }

    /// <summary>Test seam: number of waiters currently registered (after
    /// immediate-match / timeout cleanup this must return to 0).</summary>
    internal int WaiterCount
    {
        get { lock (_waiterLock) { return _waiters.Count; } }
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
