using System.Threading.Tasks;
using ACCcom.Core.Services;
using ACCcom.Core.Models;
using Xunit;

namespace ACCcom.Core.Tests;

public class ParserEngineTests
{
    private string MinimalScript => @"
var result = new List<FieldAnnotation>();
result.Add(new FieldAnnotation {
    Name = ""Test"", Offset = 0, Length = 1,
    RawHex = RawHex(0, 1),
    DisplayValue = $""0x{RawData[0]:X2}"",
    Severity = FieldSeverity.Normal
});
return result;
";

    private string CrcScript => @"
var result = new List<FieldAnnotation>();
var crc = Crc16(0, RawData.Length - 2);
var received = ToUInt16(RawData.Length - 2, false);
result.Add(new FieldAnnotation {
    Name = ""CRC"", Offset = RawData.Length - 2, Length = 2,
    RawHex = RawHex(RawData.Length - 2, 2),
    DisplayValue = $""Calc=0x{crc:X4} Recv=0x{received:X4}"",
    Severity = crc == received ? FieldSeverity.Normal : FieldSeverity.Error
});
return result;
";

    [Fact]
    public void Load_ValidScript_ReturnsTrue()
    {
        var engine = new ParserEngine();
        Assert.True(engine.Load(MinimalScript));
        Assert.Null(engine.LastError);
    }

    [Fact]
    public void Load_InvalidScript_ReturnsFalse()
    {
        var engine = new ParserEngine();
        // Completely invalid C# syntax should fail Roslyn compilation
        Assert.False(engine.Load("def foo(): pass"));
        Assert.NotNull(engine.LastError);
    }

    [Fact]
    public async Task Execute_ValidScript_ReturnsFields()
    {
        var engine = new ParserEngine();
        engine.Load(MinimalScript);
        var fields = await engine.ExecuteAsync(new byte[] { 0xAA }, DateTime.Now);
        Assert.NotNull(fields);
        Assert.Single(fields);
        Assert.Equal("Test", fields![0].Name);
        Assert.Equal("0xAA", fields[0].DisplayValue);
    }

    [Fact]
    public async Task Execute_NoScript_ReturnsNull()
    {
        var engine = new ParserEngine();
        var fields = await engine.ExecuteAsync(new byte[] { 0xAA }, DateTime.Now);
        Assert.Null(fields);
    }

    [Fact]
    public async Task Execute_ScriptWithCrc_ChecksCorrectly()
    {
        var engine = new ParserEngine();
        engine.Load(CrcScript);

        // Data with correct CRC (CRC16 of {0x01, 0x03} = 0x8042 for Modbus poly)
        var data = new byte[] { 0x01, 0x03, 0x42, 0x80 };
        var fields = await engine.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.Single(fields);
        Assert.Equal("CRC", fields![0].Name);
    }

    [Fact]
    public async Task Execute_ClearResetsEngine()
    {
        var engine = new ParserEngine();
        engine.Load(MinimalScript);
        engine.Clear();
        var fields = await engine.ExecuteAsync(new byte[] { 0xAA }, DateTime.Now);
        Assert.Null(fields);
    }

    [Fact]
    public async Task Execute_ReloadDifferentScript_Works()
    {
        var engine = new ParserEngine();
        engine.Load(MinimalScript);

        var script2 = @"
var result = new List<FieldAnnotation>();
result.Add(new FieldAnnotation {
    Name = ""Field2"", Offset = 0, Length = 1,
    RawHex = RawHex(0, 1),
    DisplayValue = ""changed"",
    Severity = FieldSeverity.Normal
});
return result;
";
        engine.Load(script2);
        var fields = await engine.ExecuteAsync(new byte[] { 0x01 }, DateTime.Now);
        Assert.NotNull(fields);
        Assert.Equal("Field2", fields![0].Name);
    }

    [Fact]
    public async Task Load_ActualSampleScript_LoadsAndExecutes()
    {
        var parserDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "parsers");
        var path = Path.Combine(parserDir, "sample.csx");
        Assert.True(File.Exists(path), $"sample.csx not found at {path}");

        var code = File.ReadAllText(path);
        var engine = new ParserEngine();
        var loaded = engine.Load(code);
        Assert.True(loaded, $"Failed to load sample.csx: {engine.LastError}");

        // AA 55 03 xx 32 - valid frame for sample.csx
        var data = new byte[] { 0xAA, 0x55, 0x03, 0x01, 0x32 };
        var fields = await engine.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.True(fields!.Count > 0, $"Expected fields but got 0. LastError: {engine.LastError}");
        Assert.Equal("帧头", fields[0].Name);
    }

    [Fact]
    public async Task Execute_ModbusTemplate_LoadsAndRuns()
    {
        var parserDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "parsers");
        var path = Path.Combine(parserDir, "modbus_rtu_template.csx");
        if (!File.Exists(path)) return; // skip if not present

        var code = File.ReadAllText(path);
        var engine = new ParserEngine();
        var loaded = engine.Load(code);
        Assert.True(loaded, $"Failed to load modbus_rtu_template.csx: {engine.LastError}");

        // Modbus RTU: addr=01, func=03, data=0001, CRC=xxxx
        var data = new byte[] { 0x01, 0x03, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00 };
        var fields = await engine.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.True(fields!.Count > 0, $"Expected fields but got 0. LastError: {engine.LastError}");
    }

    [Fact]
    public async Task Execute_EsoacV3_ControlPower_ParsesSemantically()
    {
        var parserDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "parsers");
        var path = Path.Combine(parserDir, "esoac_v3.csx");
        if (!File.Exists(path)) return;

        var code = File.ReadAllText(path);
        var engine = new ParserEngine();
        Assert.True(engine.Load(code), $"Failed to load esoac_v3.csx: {engine.LastError}");

        // ESOAC V3 frame: A5 5A + totalLen(1) + 07(1) + payloadLen(1) + 0x87(1) + RN(2) + 0x01(1=power) + 0x01(1=ON) + CRC16(2) + DD
        // Build a control power ON command
        var data = new byte[] { 0xA5, 0x5A, 0x0B, 0x07, 0x07, 0x87, 0x12, 0x34, 0x01, 0x01, 0x00, 0x00, 0xDD };
        // Fix CRC (bytes 0..9, stored at 10..11)
        ushort crc = 0xFFFF;
        for (int i = 0; i < 10; i++) { crc ^= data[i]; for (int j = 0; j < 8; j++) crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1); }
        data[10] = (byte)(crc & 0xFF);
        data[11] = (byte)(crc >> 8);

        var fields = await engine.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.True(fields!.Count > 0, $"Expected fields but got 0. LastError: {engine.LastError}");

        // Should contain semantic fields
        var cmdField = fields.FirstOrDefault(f => f.Name == "指令码");
        Assert.NotNull(cmdField);
        Assert.Contains("控制指令", cmdField!.DisplayValue);

        var subField = fields.FirstOrDefault(f => f.Name == "控制子命令");
        Assert.NotNull(subField);
        Assert.Contains("开关控制", subField!.DisplayValue);

        // Should have semantic "开关" field with ON
        var powerField = fields.FirstOrDefault(f => f.Name == "开关");
        Assert.NotNull(powerField);
        Assert.Contains("ON", powerField!.DisplayValue);
    }
}
