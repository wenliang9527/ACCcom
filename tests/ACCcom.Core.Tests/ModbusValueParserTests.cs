using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class ModbusValueParserTests
{
    // --- Coils ---

    [Fact]
    public void ParseCoilValues_ones_and_zeros()
    {
        var result = ModbusValueParser.ParseCoilValues("1,0,1");

        Assert.Equal(new[] { true, false, true }, result);
    }

    [Fact]
    public void ParseCoilValues_accepts_true_on_yes()
    {
        var result = ModbusValueParser.ParseCoilValues("true,on,yes");

        Assert.Equal(new[] { true, true, true }, result);
    }

    [Fact]
    public void ParseCoilValues_unknown_tokens_are_false()
    {
        var result = ModbusValueParser.ParseCoilValues("1,banana,off");

        Assert.Equal(new[] { true, false, false }, result);
    }

    [Fact]
    public void ParseCoilValues_trims_and_skips_empty()
    {
        var result = ModbusValueParser.ParseCoilValues(" 1 , , 0 ,true");

        Assert.Equal(new[] { true, false, true }, result);
    }

    [Fact]
    public void ParseCoilValues_empty_or_whitespace_returns_empty()
    {
        Assert.Empty(ModbusValueParser.ParseCoilValues(""));
        Assert.Empty(ModbusValueParser.ParseCoilValues("   "));
        Assert.Empty(ModbusValueParser.ParseCoilValues(null));
    }

    // --- Registers ---

    [Fact]
    public void ParseRegisterValues_decimal()
    {
        var result = ModbusValueParser.ParseRegisterValues("10,20,300");

        Assert.Equal(new ushort[] { 10, 20, 300 }, result);
    }

    [Fact]
    public void ParseRegisterValues_hex_prefix_case_insensitive()
    {
        var result = ModbusValueParser.ParseRegisterValues("0xFF,0x1A,0X10");

        Assert.Equal(new ushort[] { 255, 26, 16 }, result);
    }

    [Fact]
    public void ParseRegisterValues_mixed_decimal_and_hex()
    {
        var result = ModbusValueParser.ParseRegisterValues("5,0x0A,100");

        Assert.Equal(new ushort[] { 5, 10, 100 }, result);
    }

    [Fact]
    public void ParseRegisterValues_unparsable_token_becomes_zero()
    {
        var result = ModbusValueParser.ParseRegisterValues("1,abc,2");

        Assert.Equal(new ushort[] { 1, 0, 2 }, result);
    }

    [Fact]
    public void ParseRegisterValues_trims_and_skips_empty()
    {
        var result = ModbusValueParser.ParseRegisterValues(" 10 , ,0x20 ,40");

        Assert.Equal(new ushort[] { 10, 32, 40 }, result);
    }

    [Fact]
    public void ParseRegisterValues_empty_or_whitespace_returns_empty()
    {
        Assert.Empty(ModbusValueParser.ParseRegisterValues(""));
        Assert.Empty(ModbusValueParser.ParseRegisterValues("   "));
        Assert.Empty(ModbusValueParser.ParseRegisterValues(null));
    }

    [Fact]
    public void ParseRegisterValues_out_of_range_hex_throws()
    {
        // 0x1FFFF overflows ushort — must surface as a parse error, not a
        // silent wrap. The ViewModel's outer try/catch reports it to the user.
        Assert.Throws<OverflowException>(() => ModbusValueParser.ParseRegisterValues("0x1FFFF"));
    }
}
