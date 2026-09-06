using System;

namespace ACCcom.Core.Services;

/// <summary>
/// Replay timing math shared by the JSONL and text-format replay paths
/// (SessionRecorder.ReplaySessionAsync and the text replay in the UI).
/// Computes how long to pause between consecutive replayed entries so the
/// original inter-entry gaps are preserved, scaled by the user's speed
/// multiplier and clamped to a sane ceiling.
/// </summary>
public static class ReplayThrottle
{
    /// <summary>Maximum pause between two replayed entries, regardless of the
    /// original gap or speed multiplier — keeps a very slow capture replayable.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Returns the delay to apply between two consecutive entries: the original
    /// gap divided by <paramref name="speedMultiplier"/> (2x = half the gap), or
    /// <see cref="TimeSpan.Zero"/> when the gap is zero/negative or the speed is
    /// not positive. Clamped to <see cref="MaxDelay"/>.
    /// </summary>
    public static TimeSpan ComputeDelay(TimeSpan interEntryGap, double speedMultiplier)
    {
        if (speedMultiplier <= 0) return TimeSpan.Zero;
        if (interEntryGap <= TimeSpan.Zero) return TimeSpan.Zero;

        var adjusted = TimeSpan.FromTicks((long)(interEntryGap.Ticks / speedMultiplier));
        return adjusted > MaxDelay ? MaxDelay : adjusted;
    }
}