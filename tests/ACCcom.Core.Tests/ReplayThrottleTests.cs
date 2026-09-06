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

    [Fact]
    public void ComputeDelay_nonPositiveSpeed_returnsZero()
    {
        Assert.Equal(TimeSpan.Zero, ReplayThrottle.ComputeDelay(TimeSpan.FromSeconds(1), 0));
        Assert.Equal(TimeSpan.Zero, ReplayThrottle.ComputeDelay(TimeSpan.FromSeconds(1), -1));
    }
}