using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class SerialServiceReconnectTests
{
    [Theory]
    [InlineData(3000, 1.0, 0, 3000)]   // no backoff, first attempt
    [InlineData(3000, 1.0, 3, 3000)]   // no backoff, any attempt
    [InlineData(1000, 2.0, 0, 1000)]   // doubling, attempt 0
    [InlineData(1000, 2.0, 1, 2000)]   // doubling, attempt 1
    [InlineData(1000, 2.0, 2, 4000)]   // doubling, attempt 2
    [InlineData(1000, 2.0, 3, 8000)]   // doubling, attempt 3
    [InlineData(500, 1.5, 2, 1125)]    // 500 * 1.5^2 = 1125
    public void ComputeReconnectDelayMs_MatchesExponentialFormula(int interval, double backoff, int attempt, int expected)
    {
        var delay = SerialService.ComputeReconnectDelayMs(interval, backoff, attempt);
        Assert.Equal(expected, delay);
    }

    [Fact]
    public void ComputeReconnectDelayMs_ZeroInterval_ReturnsZero()
    {
        Assert.Equal(0, SerialService.ComputeReconnectDelayMs(0, 2.0, 3));
        Assert.Equal(0, SerialService.ComputeReconnectDelayMs(-100, 2.0, 3));
    }

    [Fact]
    public void ComputeReconnectDelayMs_NonPositiveBackoff_FallsBackToInterval()
    {
        Assert.Equal(1000, SerialService.ComputeReconnectDelayMs(1000, 0, 5));
        Assert.Equal(1000, SerialService.ComputeReconnectDelayMs(1000, -1.0, 5));
    }

    [Fact]
    public void ComputeReconnectDelayMs_NegativeAttempt_TreatedAsZero()
    {
        Assert.Equal(SerialService.ComputeReconnectDelayMs(1000, 2.0, 0),
            SerialService.ComputeReconnectDelayMs(1000, 2.0, -2));
    }

    [Fact]
    public void ComputeReconnectDelayMs_LargeBackoff_ClampsToIntMax()
    {
        var delay = SerialService.ComputeReconnectDelayMs(1000, 100.0, 200);
        Assert.Equal(int.MaxValue, delay);
    }

    [Fact]
    public void ComputeReconnectDelayMs_MonotonicWithAttempt()
    {
        int prev = 0;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var delay = SerialService.ComputeReconnectDelayMs(1000, 2.0, attempt);
            Assert.True(delay > prev, $"attempt {attempt} delay {delay} must exceed {prev}");
            prev = delay;
        }
    }
}