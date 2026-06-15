using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class PacketFilterTests
{
    private static LogEntry MakeEntry(
        string direction = "RX",
        string hex = "AA 55 01 03 02 00 01",
        string text = "OK",
        string port = "COM1",
        DateTime? time = null,
        List<FieldAnnotation>? fields = null)
    {
        return new LogEntry
        {
            Id = 1,
            Timestamp = time ?? new DateTime(2024, 1, 1, 14, 30, 15),
            Direction = direction,
            PortTag = port,
            RawHex = hex,
            Text = text,
            Fields = fields
        };
    }

    [Fact]
    public void EmptyFilter_MatchesEverything()
    {
        var engine = new PacketFilterEngine("");
        Assert.True(engine.Matches(MakeEntry()));
    }

    [Fact]
    public void Direction_EQ_RX()
    {
        var engine = new PacketFilterEngine("direction==RX");
        Assert.True(engine.Matches(MakeEntry(direction: "RX")));
        Assert.False(engine.Matches(MakeEntry(direction: "TX")));
    }

    [Fact]
    public void Direction_EQ_TX()
    {
        var engine = new PacketFilterEngine("direction==TX");
        Assert.True(engine.Matches(MakeEntry(direction: "TX")));
        Assert.False(engine.Matches(MakeEntry(direction: "RX")));
    }

    [Fact]
    public void Direction_CaseInsensitive()
    {
        var engine = new PacketFilterEngine("direction==rx");
        Assert.True(engine.Matches(MakeEntry(direction: "RX")));
    }

    [Fact]
    public void Hex_Contains()
    {
        var engine = new PacketFilterEngine("hex contains \"AA 55\"");
        Assert.True(engine.Matches(MakeEntry(hex: "AA 55 01 03")));
        Assert.False(engine.Matches(MakeEntry(hex: "BB CC 01 03")));
    }

    [Fact]
    public void Text_Contains()
    {
        var engine = new PacketFilterEngine("text contains \"OK\"");
        Assert.True(engine.Matches(MakeEntry(text: "Status: OK")));
        Assert.False(engine.Matches(MakeEntry(text: "Error")));
    }

    [Fact]
    public void Port_EQ()
    {
        var engine = new PacketFilterEngine("port==COM1");
        Assert.True(engine.Matches(MakeEntry(port: "COM1")));
        Assert.False(engine.Matches(MakeEntry(port: "COM2")));
    }

    [Fact]
    public void Time_Range()
    {
        var engine = new PacketFilterEngine("time >= \"14:30:00\" and time <= \"14:31:00\"");
        Assert.True(engine.Matches(MakeEntry(time: new DateTime(2024, 1, 1, 14, 30, 30))));
        Assert.True(engine.Matches(MakeEntry(time: new DateTime(2024, 1, 1, 14, 30, 0))));
        Assert.True(engine.Matches(MakeEntry(time: new DateTime(2024, 1, 1, 14, 31, 0))));
        Assert.False(engine.Matches(MakeEntry(time: new DateTime(2024, 1, 1, 14, 29, 59))));
        Assert.False(engine.Matches(MakeEntry(time: new DateTime(2024, 1, 1, 14, 31, 1))));
    }

    [Fact]
    public void ModbusFunc_ByHexCode()
    {
        var fields = new List<FieldAnnotation>
        {
            new() { Name = "FunctionCode", DisplayValue = "03", Offset = 0, Length = 1, RawHex = "03" }
        };
        var engine = new PacketFilterEngine("modbus.func==0x03");
        Assert.True(engine.Matches(MakeEntry(fields: fields)));
    }

    [Fact]
    public void ModbusSlave_EQ()
    {
        var fields = new List<FieldAnnotation>
        {
            new() { Name = "SlaveId", DisplayValue = "1", Offset = 0, Length = 1, RawHex = "01" }
        };
        var engine = new PacketFilterEngine("modbus.slave==1");
        Assert.True(engine.Matches(MakeEntry(fields: fields)));

        var fields5 = new List<FieldAnnotation>
        {
            new() { Name = "SlaveId", DisplayValue = "5", Offset = 0, Length = 1, RawHex = "05" }
        };
        Assert.False(engine.Matches(MakeEntry(fields: fields5)));
    }

    [Fact]
    public void And_Operator()
    {
        var engine = new PacketFilterEngine("direction==RX and text contains \"OK\"");
        Assert.True(engine.Matches(MakeEntry(direction: "RX", text: "OK")));
        Assert.False(engine.Matches(MakeEntry(direction: "TX", text: "OK")));
        Assert.False(engine.Matches(MakeEntry(direction: "RX", text: "FAIL")));
    }

    [Fact]
    public void Or_Operator()
    {
        var engine = new PacketFilterEngine("direction==RX or direction==TX");
        Assert.True(engine.Matches(MakeEntry(direction: "RX")));
        Assert.True(engine.Matches(MakeEntry(direction: "TX")));
    }

    [Fact]
    public void Not_Operator()
    {
        var engine = new PacketFilterEngine("not direction==RX");
        Assert.False(engine.Matches(MakeEntry(direction: "RX")));
        Assert.True(engine.Matches(MakeEntry(direction: "TX")));
    }

    [Fact]
    public void Parentheses_Grouping()
    {
        var engine = new PacketFilterEngine("(direction==RX or direction==TX) and text contains \"OK\"");
        Assert.True(engine.Matches(MakeEntry(direction: "RX", text: "OK")));
        Assert.True(engine.Matches(MakeEntry(direction: "TX", text: "OK")));
        Assert.False(engine.Matches(MakeEntry(direction: "RX", text: "FAIL")));
    }

    [Fact]
    public void Nested_Parentheses()
    {
        var engine = new PacketFilterEngine("not (direction==TX and text contains \"FAIL\")");
        Assert.True(engine.Matches(MakeEntry(direction: "RX", text: "FAIL")));
        Assert.True(engine.Matches(MakeEntry(direction: "RX", text: "OK")));
        Assert.True(engine.Matches(MakeEntry(direction: "TX", text: "OK")));
        Assert.False(engine.Matches(MakeEntry(direction: "TX", text: "FAIL")));
    }

    [Fact]
    public void ComplexFilter()
    {
        var fields = new List<FieldAnnotation>
        {
            new() { Name = "FunctionCode", DisplayValue = "03", Offset = 0, Length = 1, RawHex = "03" },
            new() { Name = "SlaveId", DisplayValue = "1", Offset = 1, Length = 1, RawHex = "01" }
        };
        var engine = new PacketFilterEngine(
            "direction==RX and modbus.func==0x03 and modbus.slave==1 and hex contains \"AA 55\"");

        Assert.True(engine.Matches(MakeEntry(
            direction: "RX",
            hex: "AA 55 01 03 02 00 01",
            fields: fields)));

        Assert.False(engine.Matches(MakeEntry(
            direction: "TX",
            hex: "AA 55 01 03 02 00 01",
            fields: fields)));
    }

    [Fact]
    public void NotEqual_Operator()
    {
        var engine = new PacketFilterEngine("direction!=TX");
        Assert.True(engine.Matches(MakeEntry(direction: "RX")));
        Assert.False(engine.Matches(MakeEntry(direction: "TX")));
    }

    [Fact]
    public void GreaterThan_LessThan_Operators()
    {
        var engineGt = new PacketFilterEngine("time > \"14:30:00\"");
        Assert.True(engineGt.Matches(MakeEntry(time: new DateTime(2024, 1, 1, 14, 30, 1))));
        Assert.False(engineGt.Matches(MakeEntry(time: new DateTime(2024, 1, 1, 14, 30, 0))));

        var engineLt = new PacketFilterEngine("time < \"14:31:00\"");
        Assert.True(engineLt.Matches(MakeEntry(time: new DateTime(2024, 1, 1, 14, 30, 59))));
        Assert.False(engineLt.Matches(MakeEntry(time: new DateTime(2024, 1, 1, 14, 31, 0))));
    }

    [Fact]
    public void Hex_Contains_CaseInsensitive()
    {
        var engine = new PacketFilterEngine("hex contains \"aa 55\"");
        Assert.True(engine.Matches(MakeEntry(hex: "AA 55 01 03")));
    }

    [Fact]
    public void Text_EQ()
    {
        var engine = new PacketFilterEngine("text==OK");
        Assert.True(engine.Matches(MakeEntry(text: "OK")));
        Assert.False(engine.Matches(MakeEntry(text: "OK FAIL")));
    }

    [Fact]
    public void NoFields_ModbusFilters_ReturnFalse()
    {
        var engineFunc = new PacketFilterEngine("modbus.func==0x03");
        Assert.False(engineFunc.Matches(MakeEntry(fields: null)));

        var engineSlave = new PacketFilterEngine("modbus.slave==1");
        Assert.False(engineSlave.Matches(MakeEntry(fields: null)));
    }

    [Fact]
    public void MultipleAnd_WithOr()
    {
        var engine = new PacketFilterEngine(
            "(direction==RX or direction==TX) and (text contains \"OK\" or hex contains \"AA 55\")");

        Assert.True(engine.Matches(MakeEntry(direction: "RX", text: "OK", hex: "BB CC")));
        Assert.True(engine.Matches(MakeEntry(direction: "TX", text: "NO", hex: "AA 55 01")));
        Assert.False(engine.Matches(MakeEntry(direction: "RX", text: "FAIL", hex: "BB CC")));
    }
}
