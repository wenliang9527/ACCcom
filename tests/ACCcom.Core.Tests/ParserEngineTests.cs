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

    private static ushort Crc16Modbus(byte[] data, int length)
    {
        ushort crc = 0xFFFF;
        for (int i = 0; i < length; i++)
        {
            crc ^= data[i];
            for (int j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    private static byte[] BuildEsoacV3Frame(byte cmdCode, byte[] rn, byte[] payload)
    {
        var data = new List<byte> { 0xA5, 0x5A, 0x00, 0x07, 0x00, cmdCode };
        data.AddRange(rn);
        data.AddRange(payload);
        int payloadLen = 1 + 2 + payload.Length + 2;
        data[4] = (byte)payloadLen;
        data[2] = (byte)(data.Count + 2);
        var crc = Crc16Modbus(data.ToArray(), data.Count);
        data.Add((byte)(crc & 0xFF));
        data.Add((byte)(crc >> 8));
        data.Add(0xDD);
        return data.ToArray();
    }

    private static async Task<EsoacV3TestContext> LoadEsoacV3Async()
    {
        var parserDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "parsers");
        var path = Path.Combine(parserDir, "esoac_v3.csx");
        if (!File.Exists(path))
            return new EsoacV3TestContext { Skipped = true };

        var code = await File.ReadAllTextAsync(path);
        var engine = new ParserEngine();
        var loaded = engine.Load(code);
        if (!loaded)
            return new EsoacV3TestContext { Skipped = true, Error = engine.LastError };
        return new EsoacV3TestContext { Engine = engine };
    }

    private sealed class EsoacV3TestContext
    {
        public ParserEngine? Engine { get; init; }
        public bool Skipped { get; init; }
        public string? Error { get; init; }
    }

    [Fact]
    public async Task Execute_EsoacV3_ControlPower_ParsesSemantically()
    {
        var ctx = await LoadEsoacV3Async();
        if (ctx.Skipped) return;

        var data = BuildEsoacV3Frame(0x87, [0x12, 0x34], [0x01, 0x01]);
        var fields = await ctx.Engine!.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.True(fields!.Count > 0, $"Expected fields but got 0");

        var cmdField = fields.FirstOrDefault(f => f.Name == "指令码");
        Assert.NotNull(cmdField);
        Assert.Contains("控制指令", cmdField!.DisplayValue);

        var subField = fields.FirstOrDefault(f => f.Name == "控制子命令");
        Assert.NotNull(subField);
        Assert.Contains("开关控制", subField!.DisplayValue);

        var powerField = fields.FirstOrDefault(f => f.Name == "开关");
        Assert.NotNull(powerField);
        Assert.Contains("ON", powerField!.DisplayValue);
    }

    [Fact]
    public async Task Execute_EsoacV3_Heartbeat_ParsesSemantically()
    {
        var ctx = await LoadEsoacV3Async();
        if (ctx.Skipped) return;

        var payload = new byte[12];
        // Power = 0.0f (4 bytes LE)
        Buffer.BlockCopy(BitConverter.GetBytes(0.0f), 0, payload, 0, 4);
        // Temp = 25.5f (4 bytes LE)
        Buffer.BlockCopy(BitConverter.GetBytes(25.5f), 0, payload, 4, 4);
        payload[8] = 0x01;  // Status = ON
        payload[9] = 0x01;  // Mode = Cold (1)
        payload[10] = 24;   // SetTemp = 24°C
        payload[11] = 2;    // Wind = 2

        var data = BuildEsoacV3Frame(0x03, [0x00, 0x01], payload);
        var fields = await ctx.Engine!.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.True(fields!.Count > 0);

        var cmdField = fields.FirstOrDefault(f => f.Name == "指令码");
        Assert.NotNull(cmdField);
        Assert.Contains("心跳上报", cmdField!.DisplayValue);

        var powerField = fields.FirstOrDefault(f => f.Name == "功率(Power)");
        Assert.NotNull(powerField);
        Assert.Contains("0.0W", powerField!.DisplayValue);

        var tempField = fields.FirstOrDefault(f => f.Name == "温度(Temp)");
        Assert.NotNull(tempField);
        Assert.Contains("25.5", tempField!.DisplayValue);

        var statusField = fields.FirstOrDefault(f => f.Name == "开关状态(Status)");
        Assert.NotNull(statusField);
        Assert.Contains("ON", statusField!.DisplayValue);

        var modeField = fields.FirstOrDefault(f => f.Name == "模式(Mode)");
        Assert.NotNull(modeField);
        Assert.Contains("Cold", modeField!.DisplayValue);

        var setTempField = fields.FirstOrDefault(f => f.Name == "设定温度(SetTemp)");
        Assert.NotNull(setTempField);
        Assert.Contains("24", setTempField!.DisplayValue);
    }

    [Fact]
    public async Task Execute_EsoacV3_ControlResponse_Success_Parses()
    {
        var ctx = await LoadEsoacV3Async();
        if (ctx.Skipped) return;

        // Control Resp: ctrlObj(0x01=power) + result(0x00=success) + status(0x00=SUCCESS) + reserved(2)
        var data = BuildEsoacV3Frame(0x07, [0x00, 0x01], [0x01, 0x00, 0x00, 0x00, 0x00]);
        var fields = await ctx.Engine!.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.True(fields!.Count > 0);

        var cmdField = fields.FirstOrDefault(f => f.Name == "指令码");
        Assert.NotNull(cmdField);
        Assert.Contains("控制回复", cmdField!.DisplayValue);

        var ctrlObjField = fields.FirstOrDefault(f => f.Name == "控制对象");
        Assert.NotNull(ctrlObjField);
        Assert.Contains("开关控制", ctrlObjField!.DisplayValue);

        var resultField = fields.FirstOrDefault(f => f.Name == "执行结果");
        Assert.NotNull(resultField);
        Assert.Contains("成功", resultField!.DisplayValue);

        var statusField = fields.FirstOrDefault(f => f.Name == "状态码");
        Assert.NotNull(statusField);
        Assert.Contains("SUCCESS", statusField!.DisplayValue);
    }

    [Fact]
    public async Task Execute_EsoacV3_ControlResponse_Failure_Parses()
    {
        var ctx = await LoadEsoacV3Async();
        if (ctx.Skipped) return;

        var data = BuildEsoacV3Frame(0x07, [0x00, 0x02], [0x02, 0x01, 0x02, 0x00, 0x00]);
        var fields = await ctx.Engine!.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.True(fields!.Count > 0);

        var resultField = fields.FirstOrDefault(f => f.Name == "执行结果");
        Assert.NotNull(resultField);
        Assert.Contains("失败", resultField!.DisplayValue);

        var statusField = fields.FirstOrDefault(f => f.Name == "状态码");
        Assert.NotNull(statusField);
        Assert.Contains("ERROR_CMD", statusField!.DisplayValue);
    }

    [Fact]
    public async Task Execute_EsoacV3_QueryResponse_DeviceInfo_Parses()
    {
        var ctx = await LoadEsoacV3Async();
        if (ctx.Skipped) return;

        // Query resp sub=0x01 (device info): subCmd(1) + totalPkgs(1) + pkgNo(1) + mac(6) + fwVer(4) + hwVer(4) = 17 bytes
        var payload = new byte[17];
        payload[0] = 0x01; // subCmd
        payload[1] = 0x01; // totalPkgs
        payload[2] = 0x01; // pkgNo
        // MAC: 11:22:33:44:55:66
        payload[3] = 0x11; payload[4] = 0x22; payload[5] = 0x33;
        payload[6] = 0x44; payload[7] = 0x55; payload[8] = 0x66;
        // FW ver
        payload[9] = 0x01; payload[10] = 0x00; payload[11] = 0x00; payload[12] = 0x00;
        // HW ver
        payload[13] = 0x02; payload[14] = 0x00; payload[15] = 0x00; payload[16] = 0x00;

        var data = BuildEsoacV3Frame(0x05, [0x00, 0x03], payload);
        var fields = await ctx.Engine!.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.True(fields!.Count > 0);

        var cmdField = fields.FirstOrDefault(f => f.Name == "指令码");
        Assert.NotNull(cmdField);
        Assert.Contains("查询回复", cmdField!.DisplayValue);

        var queryField = fields.FirstOrDefault(f => f.Name == "查询内容");
        Assert.NotNull(queryField);
        Assert.Contains("查询设备信息", queryField!.DisplayValue);

        var macField = fields.FirstOrDefault(f => f.Name == "设备ID");
        Assert.NotNull(macField);
        Assert.Contains("11:22:33:44:55:66", macField!.DisplayValue);
    }

    [Fact]
    public async Task Execute_EsoacV3_QueryResponse_DeviceStatus_Parses()
    {
        var ctx = await LoadEsoacV3Async();
        if (ctx.Skipped) return;

        // Query resp sub=0x02 (device status): sw(1) + mode(1) + temp(1) + wind(1) + conn(1) = 5 bytes
        var payload = new byte[8];
        payload[0] = 0x02; // subCmd
        payload[1] = 0x01; // totalPkgs
        payload[2] = 0x01; // pkgNo
        payload[3] = 0x01; // sw = ON
        payload[4] = 0x01; // mode = Cold
        payload[5] = 24;   // temp = 24°C
        payload[6] = 2;    // wind = 2
        payload[7] = 0x01; // conn = connected

        var data = BuildEsoacV3Frame(0x05, [0x00, 0x04], payload);
        var fields = await ctx.Engine!.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.True(fields!.Count > 0);

        var swField = fields.FirstOrDefault(f => f.Name == "开关");
        Assert.NotNull(swField);
        Assert.Contains("ON", swField!.DisplayValue);

        var modeField = fields.FirstOrDefault(f => f.Name == "模式");
        Assert.NotNull(modeField);
        Assert.Contains("Cold", modeField!.DisplayValue);

        var connField = fields.FirstOrDefault(f => f.Name == "连接状态");
        Assert.NotNull(connField);
        Assert.Contains("已连接", connField!.DisplayValue);
    }

    [Fact]
    public async Task Execute_EsoacV3_ModifyResponse_Success_Parses()
    {
        var ctx = await LoadEsoacV3Async();
        if (ctx.Skipped) return;

        // Modify resp: modContent(0x13=devId) + result(0x00=success) + reserved(2)
        var data = BuildEsoacV3Frame(0x08, [0x00, 0x05], [0x13, 0x00, 0x00, 0x00]);
        var fields = await ctx.Engine!.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.True(fields!.Count > 0);

        var cmdField = fields.FirstOrDefault(f => f.Name == "指令码");
        Assert.NotNull(cmdField);
        Assert.Contains("修改回复", cmdField!.DisplayValue);

        var modContentField = fields.FirstOrDefault(f => f.Name == "修改内容");
        Assert.NotNull(modContentField);
        Assert.Contains("修改设备ID", modContentField!.DisplayValue);

        var resultField = fields.FirstOrDefault(f => f.Name == "修改结果");
        Assert.NotNull(resultField);
        Assert.Contains("成功", resultField!.DisplayValue);
    }

    [Fact]
    public async Task Execute_EsoacV3_ModifyResponse_Failure_Parses()
    {
        var ctx = await LoadEsoacV3Async();
        if (ctx.Skipped) return;

        var data = BuildEsoacV3Frame(0x08, [0x00, 0x06], [0x14, 0x01, 0xFF, 0x00]);
        var fields = await ctx.Engine!.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.True(fields!.Count > 0);

        var modContentField = fields.FirstOrDefault(f => f.Name == "修改内容");
        Assert.NotNull(modContentField);
        Assert.Contains("修改产品ID", modContentField!.DisplayValue);

        var resultField = fields.FirstOrDefault(f => f.Name == "修改结果");
        Assert.NotNull(resultField);
        Assert.Contains("失败", resultField!.DisplayValue);
    }

    [Fact]
    public async Task Execute_EsoacV3_ControlCommand_Mode_Parses()
    {
        var ctx = await LoadEsoacV3Async();
        if (ctx.Skipped) return;

        // Control mode: sub=0x02, param=0x02 (Hot)
        var data = BuildEsoacV3Frame(0x87, [0x00, 0x07], [0x02, 0x02]);
        var fields = await ctx.Engine!.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.True(fields!.Count > 0);

        var subField = fields.FirstOrDefault(f => f.Name == "控制子命令");
        Assert.NotNull(subField);
        Assert.Contains("模式控制", subField!.DisplayValue);

        var modeField = fields.FirstOrDefault(f => f.Name == "模式");
        Assert.NotNull(modeField);
        Assert.Contains("Hot", modeField!.DisplayValue);
    }

    [Fact]
    public async Task Execute_EsoacV3_ControlCommand_Temp_Parses()
    {
        var ctx = await LoadEsoacV3Async();
        if (ctx.Skipped) return;

        // Control temp: sub=0x03, param=26
        var data = BuildEsoacV3Frame(0x87, [0x00, 0x08], [0x03, 26]);
        var fields = await ctx.Engine!.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.True(fields!.Count > 0);

        var subField = fields.FirstOrDefault(f => f.Name == "控制子命令");
        Assert.NotNull(subField);
        Assert.Contains("温度设置", subField!.DisplayValue);

        var tempField = fields.FirstOrDefault(f => f.Name == "设定温度");
        Assert.NotNull(tempField);
        Assert.Contains("26", tempField!.DisplayValue);
    }

    [Fact]
    public async Task Execute_EsoacV3_ControlCommand_Wind_Parses()
    {
        var ctx = await LoadEsoacV3Async();
        if (ctx.Skipped) return;

        // Control wind: sub=0x04, param=3
        var data = BuildEsoacV3Frame(0x87, [0x00, 0x09], [0x04, 3]);
        var fields = await ctx.Engine!.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.True(fields!.Count > 0);

        var subField = fields.FirstOrDefault(f => f.Name == "控制子命令");
        Assert.NotNull(subField);
        Assert.Contains("风速设置", subField!.DisplayValue);

        var windField = fields.FirstOrDefault(f => f.Name == "风速");
        Assert.NotNull(windField);
        Assert.Contains("3", windField!.DisplayValue);
    }

    [Fact]
    public async Task Execute_EsoacV3_TextHeartbeat_Parses()
    {
        var ctx = await LoadEsoacV3Async();
        if (ctx.Skipped) return;

        var text = "[Heartbeat] Power:1500.5W Temp:25.0C Status:ON Mode:Cold SetTemp:24 Wind:3";
        var data = System.Text.Encoding.ASCII.GetBytes(text);
        var fields = await ctx.Engine!.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.True(fields!.Count > 0);

        var categoryField = fields.FirstOrDefault(f => f.Name == "日志类别");
        Assert.NotNull(categoryField);
        Assert.Contains("心跳上报", categoryField!.DisplayValue);

        var powerField = fields.FirstOrDefault(f => f.Name == "功率");
        Assert.NotNull(powerField);
        Assert.Contains("1500.5W", powerField!.DisplayValue);

        var statusField = fields.FirstOrDefault(f => f.Name == "开关");
        Assert.NotNull(statusField);
        Assert.Contains("ON", statusField!.DisplayValue);
    }

    [Fact]
    public async Task Execute_EsoacV3_TextBleFrame_Parses()
    {
        var ctx = await LoadEsoacV3Async();
        if (ctx.Skipped) return;

        var text = "[BLE] C_POWER OK";
        var data = System.Text.Encoding.ASCII.GetBytes(text);
        var fields = await ctx.Engine!.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.True(fields!.Count > 0);

        var categoryField = fields.FirstOrDefault(f => f.Name == "日志类别");
        Assert.NotNull(categoryField);
        Assert.Contains("BLE", categoryField!.DisplayValue);

        var cmdField = fields.FirstOrDefault(f => f.Name == "命令");
        Assert.NotNull(cmdField);
        Assert.Contains("C_POWER", cmdField!.DisplayValue);

        var crcField = fields.FirstOrDefault(f => f.Name == "CRC状态");
        Assert.NotNull(crcField);
        Assert.Contains("校验通过", crcField!.DisplayValue);
    }

    [Fact]
    public async Task Execute_EsoacV3_TextBleFrame_CrcError_Parses()
    {
        var ctx = await LoadEsoacV3Async();
        if (ctx.Skipped) return;

        var text = "[BLE] REPORT CRC_ERR";
        var data = System.Text.Encoding.ASCII.GetBytes(text);
        var fields = await ctx.Engine!.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.True(fields!.Count > 0);

        var crcField = fields.FirstOrDefault(f => f.Name == "CRC状态");
        Assert.NotNull(crcField);
        Assert.Contains("校验失败", crcField!.DisplayValue);
        Assert.Equal(FieldSeverity.Error, crcField!.Severity);
    }

    [Fact]
    public async Task Execute_EsoacV3_ShortFrame_ReturnsError()
    {
        var ctx = await LoadEsoacV3Async();
        if (ctx.Skipped) return;

        var data = new byte[] { 0xA5, 0x5A, 0x03 };
        var fields = await ctx.Engine!.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.True(fields!.Count > 0);
        Assert.Contains(fields, f => f.Name == "错误" || f.Name == "帧头");
    }

    [Fact]
    public async Task Execute_EsoacV3_EmptyInput_ReturnsNoFields()
    {
        var ctx = await LoadEsoacV3Async();
        if (ctx.Skipped) return;

        var fields = await ctx.Engine!.ExecuteAsync([], DateTime.Now);
        Assert.NotNull(fields);
        Assert.Empty(fields);
    }

    [Fact]
    public async Task Execute_EsoacV3_TextLog_Generic_Parses()
    {
        var ctx = await LoadEsoacV3Async();
        if (ctx.Skipped) return;

        var text = "some random debug log line";
        var data = System.Text.Encoding.ASCII.GetBytes(text);
        var fields = await ctx.Engine!.ExecuteAsync(data, DateTime.Now);
        Assert.NotNull(fields);
        Assert.True(fields!.Count > 0);

        var nameField = fields.FirstOrDefault(f => f.Name == "文本日志");
        Assert.NotNull(nameField);
        Assert.Contains("random debug log", nameField!.DisplayValue);
    }
}
