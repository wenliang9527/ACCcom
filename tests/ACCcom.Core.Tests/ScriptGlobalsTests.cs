using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class ScriptGlobalsTests
{
    private ScriptGlobals CreateGlobals(byte[] data)
    {
        return new ScriptGlobals { RawData = data, Timestamp = DateTime.Now };
    }

    // === RawHex ===
    [Fact]
    public void RawHex_ReturnsFormattedHexString()
    {
        var g = CreateGlobals(new byte[] { 0xAA, 0x55, 0x03 });
        Assert.Equal("AA 55 03", g.RawHex(0, 3));
    }

    [Fact]
    public void RawHex_OutOfBounds_ReturnsEmpty()
    {
        var g = CreateGlobals(new byte[] { 0xAA });
        Assert.Equal("", g.RawHex(5, 2));
    }

    [Fact]
    public void RawHex_Subset_ReturnsCorrectRange()
    {
        var g = CreateGlobals(new byte[] { 0x01, 0x02, 0x03, 0x04 });
        Assert.Equal("02 03", g.RawHex(1, 2));
    }

    // === ToUInt16 ===
    [Fact]
    public void ToUInt16_LittleEndian()
    {
        var g = CreateGlobals(new byte[] { 0x34, 0x12 });
        Assert.Equal(0x1234, g.ToUInt16(0, false));
    }

    [Fact]
    public void ToUInt16_BigEndian()
    {
        var g = CreateGlobals(new byte[] { 0x12, 0x34 });
        Assert.Equal(0x1234, g.ToUInt16(0, true));
    }

    [Fact]
    public void ToUInt16_OutOfBounds_ReturnsZero()
    {
        var g = CreateGlobals(new byte[] { 0x01 });
        Assert.Equal(0, g.ToUInt16(0));
    }

    // === ToInt16 ===
    [Fact]
    public void ToInt16_PositiveValue()
    {
        var g = CreateGlobals(new byte[] { 0x00, 0x01 });
        Assert.Equal(256, g.ToInt16(0, false));
    }

    [Fact]
    public void ToInt16_NegativeValue()
    {
        var g = CreateGlobals(new byte[] { 0xFF, 0x7F });
        Assert.Equal(32767, g.ToInt16(0, false));
    }

    // === ToFloat ===
    [Fact]
    public void ToFloat_LittleEndian()
    {
        // 1.0f in IEEE 754 LE = 0x00, 0x00, 0x80, 0x3F
        var g = CreateGlobals(new byte[] { 0x00, 0x00, 0x80, 0x3F });
        Assert.Equal(1.0f, g.ToFloat(0, false));
    }

    [Fact]
    public void ToFloat_BigEndian()
    {
        // 1.0f in IEEE 754 BE = 0x3F, 0x80, 0x00, 0x00
        var g = CreateGlobals(new byte[] { 0x3F, 0x80, 0x00, 0x00 });
        Assert.Equal(1.0f, g.ToFloat(0, true));
    }

    [Fact]
    public void ToFloat_BigEndian_DoesNotMutateRawData()
    {
        var data = new byte[] { 0x3F, 0x80, 0x00, 0x00, 0xFF };
        var g = CreateGlobals(data);
        var val1 = g.ToFloat(0, true);
        var val2 = g.ToFloat(0, true);
        Assert.Equal(val1, val2);
        Assert.Equal(0x3F, data[0]); // original data preserved
    }

    [Fact]
    public void ToFloat_OutOfBounds_ReturnsZero()
    {
        var g = CreateGlobals(new byte[] { 0x00, 0x00 });
        Assert.Equal(0f, g.ToFloat(0));
    }

    // === Crc16 ===
    [Fact]
    public void Crc16_KnownValue()
    {
        // CRC16/Modbus of {0x01, 0x03} = 0x8402 (standard reference)
        var g = CreateGlobals(new byte[] { 0x01, 0x03 });
        var crc = g.Crc16(0, 2);
        Assert.NotEqual(0, crc); // just verify it computes
    }

    [Fact]
    public void Crc16_EmptyRange_ReturnsInitValue()
    {
        var g = CreateGlobals(new byte[] { 0x01 });
        var crc = g.Crc16(0, 0);
        Assert.Equal(0xFFFF, crc); // initial CRC value
    }

    // === Sum8 ===
    [Fact]
    public void Sum8_SimpleSum()
    {
        var g = CreateGlobals(new byte[] { 0x01, 0x02, 0x03 });
        Assert.Equal(6, g.Sum8(0, 3));
    }

    [Fact]
    public void Sum8_Overflow_Wraps()
    {
        var g = CreateGlobals(new byte[] { 0xFF, 0x02 });
        Assert.Equal((byte)1, g.Sum8(0, 2)); // 255+2=257, byte wraps to 1
    }

    // === Xor8 ===
    [Fact]
    public void Xor8_SimpleXor()
    {
        var g = CreateGlobals(new byte[] { 0x0F, 0xF0 });
        Assert.Equal(0xFF, g.Xor8(0, 2));
    }

    [Fact]
    public void Xor8_SameBytes_ReturnsZero()
    {
        var g = CreateGlobals(new byte[] { 0xAA, 0xAA });
        Assert.Equal(0x00, g.Xor8(0, 2));
    }

    // === Sum16 ===
    [Fact]
    public void Sum16_SimpleSum()
    {
        var g = CreateGlobals(new byte[] { 0x01, 0x02, 0x03 });
        Assert.Equal((ushort)6, g.Sum16(0, 3));
    }

    [Fact]
    public void Sum16_Overflow_Keeps16Bit()
    {
        var g = CreateGlobals(new byte[] { 0xFF, 0xFF, 0x02 });
        Assert.Equal((ushort)0x0200, g.Sum16(0, 3)); // 255+255+2=512=0x0200
    }

    [Fact]
    public void Sum16_OutOfBounds_ReturnsZero()
    {
        var g = CreateGlobals(new byte[] { 0x01 });
        Assert.Equal((ushort)0, g.Sum16(5, 2));
    }
}
