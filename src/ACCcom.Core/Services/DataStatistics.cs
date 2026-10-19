using System;

namespace ACCcom.Core.Services;

/// <summary>
/// Throughput / frame-rate statistics. Samples are stored in pre-allocated
/// ring slots (no per-packet heap allocation) instead of a growable queue,
/// so high frame rates do not generate garbage on the receive path.
/// </summary>
public class DataStatistics
{
    private readonly SampleRing _rxSamples = new();
    private readonly SampleRing _txSamples = new();
    private readonly IntervalRing _intervals = new();
    private long _lastFrameTimeTicks;
    private long _totalRxBytes;
    private long _totalTxBytes;
    private long _totalRxFrames;
    private long _totalTxFrames;
    private long _totalErrorFrames;

    public double RxBytesPerSecond => CalculateRate(_rxSamples);
    public double RxFramesPerSecond => CalculateFrameRate(_rxSamples);
    public double TxBytesPerSecond => CalculateRate(_txSamples);
    public double TxFramesPerSecond => CalculateFrameRate(_txSamples);
    public double ErrorRate => _totalRxFrames > 0 ? (double)_totalErrorFrames / _totalRxFrames * 100 : 0;
    public double AvgFrameIntervalMs => CalculateAvgInterval();
    public long TotalRxBytes => _totalRxBytes;
    public long TotalRxFrames => _totalRxFrames;
    public long TotalTxBytes => _totalTxBytes;
    public long TotalTxFrames => _totalTxFrames;
    public long TotalErrorFrames => _totalErrorFrames;

    public void RecordRx(int byteCount)
    {
        // A negative byte count would corrupt the totals and ring samples —
        // treat it as no data rather than poisoning the statistics.
        if (byteCount < 0) byteCount = 0;

        var now = DateTime.UtcNow;
        Interlocked.Add(ref _totalRxBytes, byteCount);
        Interlocked.Increment(ref _totalRxFrames);
        _rxSamples.Add(now.Ticks, byteCount);

        var prevTicks = Interlocked.Exchange(ref _lastFrameTimeTicks, now.Ticks);
        if (prevTicks != 0)
        {
            var interval = (now.Ticks - prevTicks) / (double)TimeSpan.TicksPerMillisecond;
            _intervals.Add(interval);
        }
    }

    public void RecordError()
    {
        Interlocked.Increment(ref _totalErrorFrames);
    }

    /// <summary>Mirror of <see cref="RecordRx"/> for outbound traffic. Used to drive the
    /// TX throughput read-out in the status bar so the user can see if the device is
    /// actually consuming what the host is sending.</summary>
    public void RecordTx(int byteCount)
    {
        if (byteCount < 0) byteCount = 0;
        Interlocked.Add(ref _totalTxBytes, byteCount);
        Interlocked.Increment(ref _totalTxFrames);
        _txSamples.Add(DateTime.UtcNow.Ticks, byteCount);
    }

    private double CalculateRate(SampleRing samples)
    {
        if (samples.Count < 2) return 0;
        var cutoff = DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(5).Ticks;
        var (bytes, _, firstTicks, count) = samples.SumSince(cutoff);
        if (count < 2 || firstTicks == 0) return 0;
        var span = (DateTime.UtcNow.Ticks - firstTicks) / (double)TimeSpan.TicksPerSecond;
        return span > 0 ? bytes / span : 0;
    }

    private double CalculateFrameRate(SampleRing samples)
    {
        if (samples.Count < 2) return 0;
        var cutoff = DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(5).Ticks;
        var (_, frames, firstTicks, count) = samples.SumSince(cutoff);
        if (count < 2 || firstTicks == 0) return 0;
        var span = (DateTime.UtcNow.Ticks - firstTicks) / (double)TimeSpan.TicksPerSecond;
        return span > 0 ? frames / span : 0;
    }

    private double CalculateAvgInterval()
    {
        var snapshot = _intervals.LastN(100);
        if (snapshot.Count < 2) return 0;
        double sum = 0;
        foreach (var v in snapshot) sum += v;
        return sum / snapshot.Count;
    }

    public void Reset()
    {
        _rxSamples.Clear();
        _txSamples.Clear();
        _intervals.Clear();
        Interlocked.Exchange(ref _totalRxBytes, 0);
        Interlocked.Exchange(ref _totalTxBytes, 0);
        Interlocked.Exchange(ref _totalRxFrames, 0);
        Interlocked.Exchange(ref _totalTxFrames, 0);
        Interlocked.Exchange(ref _totalErrorFrames, 0);
        Interlocked.Exchange(ref _lastFrameTimeTicks, 0);
    }

    /// <summary>Fixed-capacity ring of (ticks, bytes) samples. Lock-free:
    /// producers claim a slot with an Interlocked sequence and readers walk
    /// backwards from a snapshot of it. There are several concurrent producers
    /// (the serial drain task, the network bridge's receive loop, one drain per
    /// multi-port session, TX senders on the UI and background threads), so a
    /// single-writer head pointer would drop samples; the sequence makes every
    /// producer own its slot. A reader can observe a claimed-but-not-yet-written
    /// slot and read a stale value — it then breaks out of the newest-first scan
    /// and undercounts that one tick, which the next 1Hz read corrects.</summary>
    private sealed class SampleRing
    {
        private const int Capacity = 16384;
        private readonly long[] _ticks = new long[Capacity];
        private readonly int[] _bytes = new int[Capacity];
        private long _producerSeq;
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Add(long ticks, int byteCount)
        {
            var slot = Interlocked.Increment(ref _producerSeq);
            var idx = Idx(slot);
            _ticks[idx] = ticks;
            _bytes[idx] = byteCount;
            if (Volatile.Read(ref _count) < Capacity)
                Interlocked.Increment(ref _count);
        }

        /// <summary>Sums samples newer than <paramref name="cutoffTicks"/> (newest-first scan).
        /// <paramref name="firstTicks"/> is the oldest sample kept in the window.</summary>
        public (long Bytes, int Frames, long FirstTicks, int Count) SumSince(long cutoffTicks)
        {
            var seq = Volatile.Read(ref _producerSeq);
            var count = Volatile.Read(ref _count);
            long bytes = 0;
            int frames = 0;
            long firstTicks = 0;
            int seen = 0;
            for (int i = 0; i < count; i++)
            {
                var t = Volatile.Read(ref _ticks[Idx(seq - i)]);
                if (t < cutoffTicks) break;
                bytes += Volatile.Read(ref _bytes[Idx(seq - i)]);
                frames++;
                seen++;
                firstTicks = t; // newest-first scan: last assignment is the oldest in window
            }
            return (bytes, frames, firstTicks, seen);
        }

        public void Clear()
        {
            Volatile.Write(ref _producerSeq, 0);
            Volatile.Write(ref _count, 0);
        }

        /// <summary>Non-negative modulo — the sequence is reset to 0 by Clear,
        /// so a reader can snapshot a count larger than the new sequence.</summary>
        private static int Idx(long slot) => (int)(((slot % Capacity) + Capacity) % Capacity);
    }

    /// <summary>Fixed-capacity ring of inter-frame intervals (ms). Same lock-free
    /// producer-sequence design as <see cref="SampleRing"/>: intervals are
    /// recorded from every RX producer, not one thread.</summary>
    private sealed class IntervalRing
    {
        private const int Capacity = 1024;
        private readonly double[] _items = new double[Capacity];
        private long _producerSeq;
        private int _count;

        public void Add(double value)
        {
            var slot = Interlocked.Increment(ref _producerSeq);
            var idx = Idx(slot);
            _items[idx] = value;
            if (Volatile.Read(ref _count) < Capacity)
                Interlocked.Increment(ref _count);
        }

        /// <summary>Returns the most recent <paramref name="n"/> intervals, oldest first.</summary>
        public List<double> LastN(int n)
        {
            var seq = Volatile.Read(ref _producerSeq);
            var count = Volatile.Read(ref _count);
            var take = Math.Min(n, count);
            var result = new List<double>(take);
            for (int i = count - take; i < count; i++)
                result.Add(_items[Idx(seq - count + i)]);
            return result;
        }

        public void Clear()
        {
            Volatile.Write(ref _producerSeq, 0);
            Volatile.Write(ref _count, 0);
        }

        private static int Idx(long slot) => (int)(((slot % Capacity) + Capacity) % Capacity);
    }
}
