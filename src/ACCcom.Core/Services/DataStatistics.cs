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

    /// <summary>Fixed-capacity ring of (ticks, bytes, frames) samples. Thread-safe.</summary>
    private sealed class SampleRing
    {
        private const int Capacity = 16384;
        private readonly long[] _ticks = new long[Capacity];
        private readonly int[] _bytes = new int[Capacity];
        private int _head;
        private int _count;
        private readonly object _lock = new();

        public int Count
        {
            get { lock (_lock) return _count; }
        }

        public void Add(long ticks, int byteCount)
        {
            lock (_lock)
            {
                _ticks[_head] = ticks;
                _bytes[_head] = byteCount;
                _head = (_head + 1) % Capacity;
                if (_count < Capacity) _count++;
            }
        }

        /// <summary>Sums samples newer than <paramref name="cutoffTicks"/> (newest-first scan).
        /// <paramref name="firstTicks"/> is the oldest sample kept in the window.</summary>
        public (long Bytes, int Frames, long FirstTicks, int Count) SumSince(long cutoffTicks)
        {
            lock (_lock)
            {
                long bytes = 0;
                int frames = 0;
                long firstTicks = 0;
                int seen = 0;
                for (int i = 0; i < _count; i++)
                {
                    var idx = (_head - 1 - i + Capacity * 2) % Capacity;
                    var t = _ticks[idx];
                    if (t < cutoffTicks) break;
                    bytes += _bytes[idx];
                    frames++;
                    seen++;
                    firstTicks = t; // newest-first scan: last assignment is the oldest in window
                }
                return (bytes, frames, firstTicks, seen);
            }
        }

        public void Clear()
        {
            lock (_lock) { _head = 0; _count = 0; }
        }
    }

    /// <summary>Fixed-capacity ring of inter-frame intervals (ms).</summary>
    private sealed class IntervalRing
    {
        private const int Capacity = 1024;
        private readonly double[] _items = new double[Capacity];
        private int _head;
        private int _count;
        private readonly object _lock = new();

        public void Add(double value)
        {
            lock (_lock)
            {
                _items[_head] = value;
                _head = (_head + 1) % Capacity;
                if (_count < Capacity) _count++;
            }
        }

        /// <summary>Returns the most recent <paramref name="n"/> intervals, oldest first.</summary>
        public List<double> LastN(int n)
        {
            lock (_lock)
            {
                var take = Math.Min(n, _count);
                var result = new List<double>(take);
                for (int i = _count - take; i < _count; i++)
                {
                    result.Add(_items[(_head - _count + i + Capacity * 2) % Capacity]);
                }
                return result;
            }
        }

        public void Clear()
        {
            lock (_lock) { _head = 0; _count = 0; }
        }
    }
}