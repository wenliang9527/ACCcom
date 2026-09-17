using System;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class ReplayThrottleTests
{
    [Fact]
    public void ComputeDelay_normalSpeed_preservesGap()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(1000), ReplayThrottle.ComputeDelay(TimeSpan.FromMilliseconds(1000), 1.0));
    }

    [Fact]
    public void ComputeDelay_doubleSpeed_halvesGap()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(500), ReplayThrottle.ComputeDelay(TimeSpan.FromMilliseconds(1000), 2.0));
    }

    [Fact]
    public void ComputeDelay_halfSpeed_doublesGap()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(2000), ReplayThrottle.ComputeDelay(TimeSpan.FromMilliseconds(1000), 0.5));
    }

    [Fact]
    public void ComputeDelay_clampsToMax()
    {
        Assert.Equal(ReplayThrottle.MaxDelay, ReplayThrottle.ComputeDelay(TimeSpan.FromMinutes(1), 1.0));
        Assert.Equal(TimeSpan.FromSeconds(5), ReplayThrottle.ComputeDelay(TimeSpan.FromSeconds(10), 2.0));
    }

    [Fact]
    public void ComputeDelay_zeroOrNegativeGap_returnsZero()
    {
        Assert.Equal(TimeSpan.Zero, ReplayThrottle.ComputeDelay(TimeSpan.Zero, 1.0));
        Assert.Equal(TimeSpan.Zero, ReplayThrottle.ComputeDelay(TimeSpan.FromMilliseconds(-5), 1.0));
    }

    [Theory]
    [InlineData(1L, double.Epsilon)]
    [InlineData(10000000L, 1e-300)]
    [InlineData(long.MaxValue, 1.0)]
    public void ComputeDelay_extremeScaledGap_clampsBeforeIntegerConversion(long ticks, double speed)
    {
        Assert.Equal(ReplayThrottle.MaxDelay, ReplayThrottle.ComputeDelay(TimeSpan.FromTicks(ticks), speed));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ComputeDelay_nonFiniteSpeed_returnsZero(double speed)
    {
        Assert.Equal(TimeSpan.Zero, ReplayThrottle.ComputeDelay(TimeSpan.FromSeconds(1), speed));
    }

    [Theory]
    [InlineData(1L, 2.0, 0L)]
    [InlineData(3L, 2.0, 1L)]
    [InlineData(49999999L, 1.0, 49999999L)]
    [InlineData(50000000L, 1.0, 50000000L)]
    public void ComputeDelay_tickPrecisionAndCeiling_arePreserved(long ticks, double speed, long expectedTicks)
    {
        Assert.Equal(TimeSpan.FromTicks(expectedTicks), ReplayThrottle.ComputeDelay(TimeSpan.FromTicks(ticks), speed));
    }

    [Fact]
    public void ComputeDelay_nonPositiveSpeed_returnsZero()
    {
        Assert.Equal(TimeSpan.Zero, ReplayThrottle.ComputeDelay(TimeSpan.FromSeconds(1), 0));
        Assert.Equal(TimeSpan.Zero, ReplayThrottle.ComputeDelay(TimeSpan.FromSeconds(1), -1));
    }
}