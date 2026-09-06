using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class StatusLineFormatterTests
{
    [Fact]
    public void FormatThroughput_CombinesUnits()
    {
        Assert.Equal("12.3 B/s | 45.6 fps", StatusLineFormatter.FormatThroughput(12.3, 45.6));
    }

    [Fact]
    public void FormatThroughput_RoundsToTenths()
    {
        Assert.Equal("0.1 B/s | 9.9 fps", StatusLineFormatter.FormatThroughput(0.06, 9.95));
    }

    [Fact]
    public void FormatThroughput_NaNBecomesZero()
    {
        Assert.Equal("0.0 B/s | 0.0 fps", StatusLineFormatter.FormatThroughput(double.NaN, double.PositiveInfinity));
        Assert.Equal("0.0 B/s | 0.0 fps", StatusLineFormatter.FormatThroughput(double.NegativeInfinity, double.NaN));
    }

    [Fact]
    public void FormatErrorRate_AppendsPercent()
    {
        Assert.Equal("0.7%", StatusLineFormatter.FormatErrorRate(0.666));
        Assert.Equal("0.0%", StatusLineFormatter.FormatErrorRate(double.NaN));
    }

    [Fact]
    public void FormatFrameInterval_AppendsMilliseconds()
    {
        Assert.Equal("1.2 ms", StatusLineFormatter.FormatFrameInterval(1.234));
        Assert.Equal("0.0 ms", StatusLineFormatter.FormatFrameInterval(double.PositiveInfinity));
    }
}