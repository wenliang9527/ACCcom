using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class ModbusRegisterDecoderTests
{
    [Fact]
    public void Decode_single_register_big_endian()
    {
        var result = ModbusRegisterDecoder.Decode([0x12, 0x34], baseAddr: 0);

        Assert.Single(result);
        Assert.Equal(0, result[0].Address);
        Assert.Equal(0x1234, result[0].Value);
    }

    [Fact]
    public void Decode_multiple_registers_increment_addresses()
    {
        var result = ModbusRegisterDecoder.Decode([0x00, 0x01, 0x00, 0x02, 0x00, 0x03], baseAddr: 10);

        Assert.Equal(3, result.Count);
        Assert.Equal(10, result[0].Address);
        Assert.Equal(1, result[0].Value);
        Assert.Equal(11, result[1].Address);
        Assert.Equal(2, result[1].Value);
        Assert.Equal(12, result[2].Address);
        Assert.Equal(3, result[2].Value);
    }

    [Fact]
    public void Decode_zero_values()
    {
        var result = ModbusRegisterDecoder.Decode([0x00, 0x00], baseAddr: 0);

        Assert.Equal(0, result[0].Value);
    }

    [Fact]
    public void Decode_max_uint16()
    {
        var result = ModbusRegisterDecoder.Decode([0xFF, 0xFF], baseAddr: 0);

        Assert.Equal(65535, result[0].Value);
    }

    [Fact]
    public void Decode_odd_trailing_byte_dropped()
    {
        var result = ModbusRegisterDecoder.Decode([0x00, 0x01, 0x00], baseAddr: 0);

        // The protocol guarantees even payloads; the trailing 0x00 is framing
        // garbage and must not become a register.
        Assert.Single(result);
        Assert.Equal(1, result[0].Value);
    }

    [Fact]
    public void Decode_empty_payload_returns_empty()
    {
        Assert.Empty(ModbusRegisterDecoder.Decode([], baseAddr: 0));
    }

    [Fact]
    public void Decode_single_byte_returns_empty()
    {
        Assert.Empty(ModbusRegisterDecoder.Decode([0x00], baseAddr: 0));
    }

    [Fact]
    public void Decode_base_addr_offset_with_large_values()
    {
        var result = ModbusRegisterDecoder.Decode([0xAB, 0xCD], baseAddr: 400);

        Assert.Equal(400, result[0].Address);
        Assert.Equal(0xABCD, result[0].Value);
    }

    [Fact]
    public void RegisterItem_display_properties()
    {
        var item = ModbusRegisterDecoder.Decode([0x0A, 0x0B], baseAddr: 0)[0];

        Assert.Equal("0x0A0B", item.Hex);
        Assert.Equal((ushort)0x0A0B, item.Dec);
        Assert.Equal("0000101000001011", item.Binary);
    }

    [Fact]
    public void Decode_many_registers()
    {
        var data = new byte[64];
        for (int i = 0; i < 64; i++) data[i] = (byte)(i + 1);

        var result = ModbusRegisterDecoder.Decode(data, baseAddr: 0);

        Assert.Equal(32, result.Count);
        // First register: bytes 1,2 -> 0x0102
        Assert.Equal(0x0102, result[0].Value);
        // Last register: bytes 63,64 -> 0x3F40
        Assert.Equal(0x3F40, result[31].Value);
        Assert.Equal(31, result[31].Address);
    }
}
