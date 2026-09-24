using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class SerialTimingTests
{
    [Fact]
    public void MinIntervalMs_UsesEightNOneFrameMath()
    {
        // 1 byte @ 115200 → ceil(0.0868) + 1 = 2
        Assert.Equal(2, SerialTiming.MinIntervalMs(1, 115200));
        // 16 bytes @ 9600 → ceil(16.67) + 1 = 18
        Assert.Equal(18, SerialTiming.MinIntervalMs(16, 9600));
        // 16 bytes @ 115200 → ceil(1.39) + 1 = 3
        Assert.Equal(3, SerialTiming.MinIntervalMs(16, 115200));
    }

    [Fact]
    public void MinIntervalMs_InvalidBaud_FallsBackToDefault()
    {
        int expected = SerialTiming.MinIntervalMs(1, SerialTiming.DefaultBaudRate);
        Assert.Equal(expected, SerialTiming.MinIntervalMs(1, 0));
        Assert.Equal(expected, SerialTiming.MinIntervalMs(1, -9600));
    }

    [Fact]
    public void MinIntervalMs_NonPositiveByteLength_UsesOneByteFloor()
    {
        Assert.Equal(SerialTiming.MinIntervalMs(1, 9600), SerialTiming.MinIntervalMs(0, 9600));
        Assert.Equal(SerialTiming.MinIntervalMs(1, 9600), SerialTiming.MinIntervalMs(-5, 9600));
    }

    [Fact]
    public void MinIntervalMs_FasterBaud_ProducesSmallerOrEqualInterval()
    {
        Assert.True(SerialTiming.MinIntervalMs(64, 115200) <= SerialTiming.MinIntervalMs(64, 9600));
    }
}
