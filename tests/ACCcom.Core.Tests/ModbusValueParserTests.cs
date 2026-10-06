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
    public void ParseCoilValues_empty_or_whitespace_returns_null()
    {
        // Null (not empty) surfaces the "values must not be null or empty" error
        // at parse time, matching ModbusService's multi-write contract.
        Assert.Null(ModbusValueParser.ParseCoilValues(""));
        Assert.Null(ModbusValueParser.ParseCoilValues("   "));
        Assert.Null(ModbusValueParser.ParseCoilValues(null));
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
    public void ParseRegisterValues_empty_or_whitespace_returns_null()
    {
        Assert.Null(ModbusValueParser.ParseRegisterValues(""));
        Assert.Null(ModbusValueParser.ParseRegisterValues("   "));
        Assert.Null(ModbusValueParser.ParseRegisterValues(null));
    }

    [Fact]
    public void ParseRegisterValues_out_of_range_hex_becomes_zero()
    {
        // 0x1FFFF 超出 ushort — 与十进制 "70000 → 0" 同一条 base 无关规则:
        // 不可解析一律为 0,不抛(此前 hex 路径用抛异常的 ushort.Parse,
        // 与同方法十进制分支、XML 文档承诺三方不一致)。
        var result = ModbusValueParser.ParseRegisterValues("1,0x1FFFF,2");

        Assert.Equal(new ushort[] { 1, 0, 2 }, result);
    }

    [Fact]
    public void ParseRegisterValues_malformed_hex_becomes_zero()
    {
        // 非法 hex 字符与前缀后为空,同样落入 0 而不是 FormatException。
        var result = ModbusValueParser.ParseRegisterValues("0xGG,0x,5");

        Assert.Equal(new ushort[] { 0, 0, 5 }, result);
    }
}
