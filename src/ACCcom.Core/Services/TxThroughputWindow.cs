using System.Collections.Concurrent;

namespace ACCcom.Core.Services;

/// <summary>Sliding-window TX throughput calculator: callers record (time,
/// bytes) frames, the window keeps a bounded retention horizon, and the rate
/// is computed over a shorter evaluation window. Rates read zero unless at
/// least two frames span a positive interval — the same contract the stats
/// panel previously implemented inline.</summary>
public sealed class TxThroughputWindow
{
    private readonly ConcurrentQueue<(DateTime Time, int Bytes)> _samples = new();
    private readonly TimeSpan _retention;
    private readonly TimeSpan _evaluation;

    public TxThroughputWindow(TimeSpan? retention = null, TimeSpan? evaluation = null)
    {
        // Non-positive windows would trim every sample on arrival (or divide
        // by a zero span); fall back to the panel's defaults instead.
        _retention = retention is { } r && r > TimeSpan.Zero ? r : TimeSpan.FromSeconds(10);
        _evaluation = evaluation is { } e && e > TimeSpan.Zero ? e : TimeSpan.FromSeconds(5);
    }

    public void Record(int byteCount) => Record(byteCount, DateTime.Now);

    public void Record(int byteCount, DateTime now)
    {
        _samples.Enqueue((now, byteCount));
        Trim(now);
    }

    /// <summary>(bytesPerSecond, framesPerSecond) over the evaluation window
    /// as of <paramref name="now"/>. Fewer than two in-window frames or a
    /// zero/negative span reports (0, 0) rather than dividing by zero.</summary>
    public (double BytesPerSec, double FramesPerSec) ComputeRate(DateTime now)
    {
        Trim(now);
        var cutoff = now - _evaluation;
        long totalBytes = 0;
        int frameCount = 0;
        DateTime first = default;
        foreach (var s in _samples)
        {
            if (s.Time >= cutoff)
            {
                if (frameCount == 0) first = s.Time;
                totalBytes += s.Bytes;
                frameCount++;
            }
        }
        if (frameCount < 2) return (0, 0);
        var span = (now - first).TotalSeconds;
        return span > 0 ? (totalBytes / span, frameCount / span) : (0, 0);
    }

    private void Trim(DateTime now)
    {
        var cutoff = now - _retention;
        while (_samples.TryPeek(out var oldest) && oldest.Time < cutoff)
            _samples.TryDequeue(out _);
    }
}
