using System.Collections.Concurrent;

namespace ACCcom.Core.Services;

public class DataStatistics
{
    private readonly ConcurrentQueue<TimestampedSample> _rxSamples = new();
    private readonly ConcurrentQueue<TimestampedSample> _errorSamples = new();
    private readonly ConcurrentQueue<double> _intervals = new();
    private DateTime? _lastFrameTime;
    private long _totalRxBytes;
    private long _totalRxFrames;
    private long _totalErrorFrames;

    public double RxBytesPerSecond => CalculateRate(_rxSamples);
    public double RxFramesPerSecond => CalculateFrameRate(_rxSamples);
    public double ErrorRate => _totalRxFrames > 0 ? (double)_totalErrorFrames / _totalRxFrames * 100 : 0;
    public double AvgFrameIntervalMs => CalculateAvgInterval();
    public long TotalRxBytes => _totalRxBytes;
    public long TotalRxFrames => _totalRxFrames;
    public long TotalErrorFrames => _totalErrorFrames;

    public void RecordRx(int byteCount)
    {
        var now = DateTime.Now;
        Interlocked.Add(ref _totalRxBytes, byteCount);
        Interlocked.Increment(ref _totalRxFrames);
        _rxSamples.Enqueue(new TimestampedSample(now, byteCount));

        if (_lastFrameTime.HasValue)
        {
            var interval = (now - _lastFrameTime.Value).TotalMilliseconds;
            _intervals.Enqueue(interval);
            while (_intervals.Count > 1000) _intervals.TryDequeue(out _);
        }
        _lastFrameTime = now;

        // Cleanup old samples (keep last 10 seconds)
        CleanupOldSamples(_rxSamples, TimeSpan.FromSeconds(10));
    }

    public void RecordError()
    {
        Interlocked.Increment(ref _totalErrorFrames);
        _errorSamples.Enqueue(new TimestampedSample(DateTime.Now, 1));
        CleanupOldSamples(_errorSamples, TimeSpan.FromSeconds(10));
    }

    private double CalculateRate(ConcurrentQueue<TimestampedSample> samples)
    {
        if (samples.IsEmpty) return 0;
        var now = DateTime.Now;
        var cutoff = now - TimeSpan.FromSeconds(5);
        long totalBytes = 0;
        int count = 0;
        DateTime first = default;
        foreach (var s in samples)
        {
            if (s.Timestamp >= cutoff)
            {
                if (count == 0) first = s.Timestamp;
                totalBytes += s.Value;
                count++;
            }
        }
        if (count < 2) return 0;
        var span = (now - first).TotalSeconds;
        return span > 0 ? totalBytes / span : 0;
    }

    private double CalculateFrameRate(ConcurrentQueue<TimestampedSample> samples)
    {
        if (samples.IsEmpty) return 0;
        var now = DateTime.Now;
        var cutoff = now - TimeSpan.FromSeconds(5);
        int count = 0;
        DateTime first = default;
        foreach (var s in samples)
        {
            if (s.Timestamp >= cutoff)
            {
                if (count == 0) first = s.Timestamp;
                count++;
            }
        }
        if (count < 2) return 0;
        var span = (now - first).TotalSeconds;
        return span > 0 ? count / span : 0;
    }

    private double CalculateAvgInterval()
    {
        if (_intervals.IsEmpty) return 0;
        int count = 0;
        double sum = 0;
        foreach (var interval in _intervals)
        {
            if (count >= _intervals.Count - 100)
            {
                sum += interval;
                count++;
            }
        }
        return count > 0 ? sum / count : 0;
    }

    private void CleanupOldSamples(ConcurrentQueue<TimestampedSample> queue, TimeSpan maxAge)
    {
        var cutoff = DateTime.Now - maxAge;
        while (queue.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
            queue.TryDequeue(out _);
    }

    public void Reset()
    {
        while (_rxSamples.TryDequeue(out _)) { }
        while (_errorSamples.TryDequeue(out _)) { }
        while (_intervals.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _totalRxBytes, 0);
        Interlocked.Exchange(ref _totalRxFrames, 0);
        Interlocked.Exchange(ref _totalErrorFrames, 0);
        _lastFrameTime = null;
    }

    private record TimestampedSample(DateTime Timestamp, int Value);
}
