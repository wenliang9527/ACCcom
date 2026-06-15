using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class CrcHelperTests
{
    [Fact]
    public void Crc16_KnownVector_ReturnsCorrectValue()
    {
        var data = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x0A };
        var crc = CrcHelper.Crc16(data);

        Assert.True(crc > 0);
    }

    [Fact]
    public void Crc16_EmptyData_ReturnsFFFF()
    {
        var data = Array.Empty<byte>();
        var crc = CrcHelper.Crc16(data);

        Assert.Equal((ushort)0xFFFF, crc);
    }

    [Fact]
    public void Crc16_WithOffset_WorksCorrectly()
    {
        var data = new byte[] { 0xFF, 0x01, 0x03, 0x00, 0x00, 0x00, 0x0A };
        var crc1 = CrcHelper.Crc16(data, 1, 6);
        var crc2 = CrcHelper.Crc16(data.AsSpan(1, 6));

        Assert.Equal(crc1, crc2);
    }

    [Fact]
    public void Sum8_CalculatesCorrectly()
    {
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var sum = CrcHelper.Sum8(data);

        Assert.Equal((byte)0x0A, sum);
    }

    [Fact]
    public void Sum8_EmptyData_ReturnsZero()
    {
        var data = Array.Empty<byte>();
        var sum = CrcHelper.Sum8(data);

        Assert.Equal((byte)0, sum);
    }

    [Fact]
    public void Xor8_CalculatesCorrectly()
    {
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var xor = CrcHelper.Xor8(data);

        Assert.Equal((byte)(0x01 ^ 0x02 ^ 0x03 ^ 0x04), xor);
    }

    [Fact]
    public void Xor8_EmptyData_ReturnsZero()
    {
        var data = Array.Empty<byte>();
        var xor = CrcHelper.Xor8(data);

        Assert.Equal((byte)0, xor);
    }

    [Fact]
    public void Sum16_CalculatesCorrectly()
    {
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var sum = CrcHelper.Sum16(data);

        Assert.Equal((ushort)0x0A, sum);
    }

    [Fact]
    public void Sum16_EmptyData_ReturnsZero()
    {
        var data = Array.Empty<byte>();
        var sum = CrcHelper.Sum16(data);

        Assert.Equal((ushort)0, sum);
    }
}
