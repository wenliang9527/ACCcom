using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class TxThroughputWindowTests
{
    private static readonly DateTime T0 = new(2026, 9, 11, 10, 0, 0);

    [Fact]
    public void ComputeRate_EmptyWindow_ReturnsZero()
    {
        var window = new TxThroughputWindow();

        Assert.Equal((0, 0), window.ComputeRate(T0));
    }

    [Fact]
    public void ComputeRate_FewerThanTwoFrames_ReturnsZero()
    {
        var window = new TxThroughputWindow();
        window.Record(100, T0);

        Assert.Equal((0, 0), window.ComputeRate(T0.AddSeconds(2)));
    }

    [Fact]
    public void ComputeRate_TwoFramesSpanningTime_ComputesRates()
    {
        var window = new TxThroughputWindow();
        window.Record(100, T0);
        window.Record(300, T0.AddSeconds(2));

        // Span = first in-window sample to now = 2s; 400 bytes / 2s = 200 B/s,
        // 2 frames / 2s = 1 frame/s.
        Assert.Equal((200.0, 1.0), window.ComputeRate(T0.AddSeconds(2)));
    }

    [Fact]
    public void ComputeRate_SampleOutsideEvaluationWindow_Ignored()
    {
        var window = new TxThroughputWindow();
        window.Record(100, T0);                      // 6s before "now" — outside the 5s window
        window.Record(300, T0.AddSeconds(6));

        // Only the second frame is in-window → fewer than two frames → zero.
        Assert.Equal((0, 0), window.ComputeRate(T0.AddSeconds(6)));
    }

    [Fact]
    public void Record_TrimsSamplesPastRetention()
    {
        var window = new TxThroughputWindow();
        window.Record(100, T0);
        window.Record(100, T0.AddSeconds(11));       // retention 10s — first sample evicted

        // At 11s, the eval window (5s) holds only the second frame → zero.
        Assert.Equal((0, 0), window.ComputeRate(T0.AddSeconds(11)));
    }

    [Fact]
    public void ComputeRate_ZeroSpanFrames_ReturnsZero()
    {
        var window = new TxThroughputWindow();
        window.Record(100, T0);
        window.Record(300, T0);                      // same timestamp → span 0

        Assert.Equal((0, 0), window.ComputeRate(T0));
    }

    [Fact]
    public void Constructor_NonPositiveWindows_FallBackToDefaults()
    {
        // A zero/negative window would trim every sample on arrival; the
        // defaults (10s retention / 5s evaluation) must apply instead.
        var window = new TxThroughputWindow(TimeSpan.Zero, TimeSpan.FromSeconds(-5));
        window.Record(100, T0);
        window.Record(300, T0.AddSeconds(2));

        Assert.Equal((200.0, 1.0), window.ComputeRate(T0.AddSeconds(2)));
    }
}
