using System;

namespace ACCcom.Core.Services;

/// <summary>
/// Formats DataStatistics values into the status-bar lines shown under the
/// RX/TX counters. Pulled out of the view model so the presentation strings
/// are unit-testable without a UI thread. All overloads treat NaN and
/// ±Infinity as "no data" (0.0) — formatting NaN with ":F1" would otherwise
/// surface a literal "NaN" in the status bar.
/// </summary>
public static class StatusLineFormatter
{
    private const double NoData = 0.0;

    /// <summary>"12.3 B/s | 45.6 fps" — one line for the RX/TX throughput block.</summary>
    public static string FormatThroughput(double bytesPerSecond, double framesPerSecond)
        => $"{Safe(bytesPerSecond):F1} B/s | {Safe(framesPerSecond):F1} fps";

    /// <summary>"0.7%" — error-frame share of received frames.</summary>
    public static string FormatErrorRate(double errorRate)
        => $"{Safe(errorRate):F1}%";

    /// <summary>"1.2 ms" — average inter-frame gap.</summary>
    public static string FormatFrameInterval(double intervalMs)
        => $"{Safe(intervalMs):F1} ms";

    private static double Safe(double value)
        => double.IsNaN(value) || double.IsInfinity(value) ? NoData : value;
}