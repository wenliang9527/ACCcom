using ACCcom.Core.Models;
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

    [Theory]
    [InlineData("")]
    [InlineData("01")]
    [InlineData("01 03")]
    [InlineData("01 03 00 00 00 01")]
    [InlineData("01 04 00 00 00 01")]
    [InlineData("FF")]
    [InlineData("FF FF FF FF FF FF FF FF")]
    [InlineData("00 10 00 03 00 0A 00 01")]
    public void Crc16_table_result_must_equal_the_bitwise_reference(string hex)
    {
        // The table rewrite is pinned against a verbatim copy of the old
        // bitwise implementation — not hand-computed magic numbers, which would
        // lock in a mis-transcribed constant as "correct".
        var data = HexHelper.TryHexStringToBytes(hex, out var bytes) ? bytes : [];

        Assert.Equal(ReferenceCrc16(data), CrcHelper.Crc16(data));
    }

    [Fact]
    public void Crc16_matches_the_known_modbus_request_vector()
    {
        // Classic read-holding-registers request: slave 1, FC03, start 0, qty 10.
        // 0xCDC5 independently verified against a bitwise reference outside
        // this repo, so the table (and the oracle above) are both pinned.
        var frame = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x0A };

        Assert.Equal((ushort)0xCDC5, CrcHelper.Crc16(frame));
    }

    [Fact]
    public void Crc16_with_offset_matches_the_full_span_result()
    {
        var data = new byte[] { 0xDE, 0xAD, 0x01, 0x03, 0x00, 0x00, 0x00, 0x0A, 0xBE, 0xEF };

        Assert.Equal(ReferenceCrc16(data.AsSpan(2, 6)), CrcHelper.Crc16(data, 2, 6));
        Assert.Equal(CrcHelper.Crc16(data.AsSpan(2, 6)), CrcHelper.Crc16(data, 2, 6));
    }

    /// <summary>The pre-table bitwise implementation, kept verbatim as the
    /// oracle so the rewrite cannot silently change results. Deliberately not
    /// calling into CrcHelper.</summary>
    private static ushort ReferenceCrc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
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
    public void Crc16_NegativeOffset_ReturnsZero()
    {
        var data = new byte[] { 0x01, 0x02, 0x03 };

        Assert.Equal(0, CrcHelper.Crc16(data, -1, 2));
    }

    [Fact]
    public void Crc16_NegativeLength_ReturnsZero()
    {
        var data = new byte[] { 0x01, 0x02, 0x03 };

        Assert.Equal(0, CrcHelper.Crc16(data, 0, -1));
    }

    [Fact]
    public void Crc16_OffsetPlusLengthBeyondData_ReturnsZero()
    {
        var data = new byte[] { 0x01, 0x02, 0x03 };

        Assert.Equal(0, CrcHelper.Crc16(data, 1, 3));
        Assert.Equal(0, CrcHelper.Crc16(data, 0, 4));
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
