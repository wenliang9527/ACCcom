# ESOAC V3 Protocol Parser Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Create a `.csx` protocol parser for the ESOAC V3.0 communication protocol, enabling ACCcom to decode and display frames from the FR801xH BLE + ML307A 4G IR AC controller.

**Architecture:** Single `.csx` C# script file placed in `src/ACCcom/parsers/`. The parser detects frame type by command code, validates CRC, and parses fields per the ESOAC V3 protocol specification.

**Tech Stack:** C# Script (.csx), using ScriptGlobals helpers (RawHex, ToUInt16, ToFloat, Crc16, Sum8, Xor8)

**Plan saved to:** `docs/superpowers/plans/2026-06-11-esoac-v3-parser.md`

---

### Task 1: Create the ESOAC V3 protocol parser .csx file

**Files:**
- Create: `src/ACCcom/parsers/esoac_v3.csx`

- [ ] **Step 1: Write the ESOAC V3 parser with frame structure detection + CRC validation**

The parser needs to handle:

1. **Frame structure**: `A5 5A + DataLen + 0x07 + PayloadLen + CmdCode + RN(2) + [SubCmd] + Params + CRC16 + DD`
2. **Command codes** and their sub-commands
3. **Heartbeat (0x03)**: 12-byte payload with Power(float), Temp(float), Status(byte), Mode(byte), SetTemp(byte), Wind(byte)
4. **Query cmd (0x85)** / **Ctrl cmd (0x87)** / **Modify cmd (0x88)** with sub-command names
5. **Query responses (0x05)**: Parse based on sub-command
6. **Control response (0x07)**: Status codes
7. **Modify response (0x08)**: Success/fail
8. **Status code mapping**: 0x00=SUCCESS, 0x01=ERROR_PARAM, 0x02=ERROR_CMD, 0x03=ERROR_CRC, 0x04=ERROR_BUSY, 0x05=ERROR_STORAGE, 0x10=LISTEN_OFF, 0x11=LEARN_MODE, 0x12=NO_LEARN_DATA, 0xFF=ERROR_FAIL

```csharp
// ESOAC V3.0 通讯协议解析器
// 协议版本: ESOAC V3.0
// 设备: FR801xH BLE + ML307A 4G 红外空调控制器
// 帧结构: A5 5A + DataLen + 0x07 + PayloadLen + CmdCode + RN(2) + [SubCmd] + Params + CRC16 + DD

var fields = new List<FieldAnnotation>();

if (RawData.Length < 9)
{
    fields.Add(new FieldAnnotation { Name = "错误", Offset = 0, Length = RawData.Length, RawHex = RawHex(0, RawData.Length), DisplayValue = "帧长度不足9字节", Color = "#EF5350", Severity = FieldSeverity.Error });
    return fields;
}

// ===== 帧头 =====
int offset = 0;
var header = RawHex(offset, 2);
bool headerOk = RawData[0] == 0xA5 && RawData[1] == 0x5A;
fields.Add(new FieldAnnotation
{
    Name = "帧头", Offset = offset, Length = 2, RawHex = header,
    DisplayValue = headerOk ? "A5 5A (正确)" : $"A5 5A (错误: 实际={header})",
    Color = headerOk ? "#3478F6" : "#EF5350",
    Severity = headerOk ? FieldSeverity.Normal : FieldSeverity.Error
});
offset += 2;

// ===== 数据总长度 =====
var totalLen = RawData[offset];
fields.Add(new FieldAnnotation { Name = "数据总长", Offset = offset, Length = 1, RawHex = RawHex(offset, 1), DisplayValue = $"{totalLen} 字节" });
offset += 1;

if (offset >= RawData.Length) return fields;

// ===== 内容类型 =====
var contentType = RawData[offset];
bool contentTypeOk = contentType == 0x07;
fields.Add(new FieldAnnotation
{
    Name = "内容类型", Offset = offset, Length = 1, RawHex = RawHex(offset, 1),
    DisplayValue = contentTypeOk ? "0x07 (结构化数据)" : $"0x{contentType:X2} (未知)",
    Color = contentTypeOk ? "#3478F6" : "#FF9800",
    Severity = contentTypeOk ? FieldSeverity.Normal : FieldSeverity.Warning
});
offset += 1;

if (offset >= RawData.Length) return fields;

// ===== 有效数据长 =====
var payloadLen = RawData[offset];
fields.Add(new FieldAnnotation { Name = "有效数据长", Offset = offset, Length = 1, RawHex = RawHex(offset, 1), DisplayValue = $"{payloadLen} 字节" });
offset += 1;

if (offset >= RawData.Length) return fields;

// ===== 指令码 =====
var cmdCode = RawData[offset];

string cmdName = cmdCode switch
{
    0x03 => "心跳上报(REPORT)",
    0x05 => "查询回复(QUERY_RESP)",
    0x07 => "控制回复(CTRL_RESP)",
    0x08 => "修改回复(MODIFY_RESP)",
    0x85 => "查询指令(QUERY)",
    0x87 => "控制指令(CTRL)",
    0x88 => "修改指令(MODIFY)",
    _ => $"未知(0x{cmdCode:X2})"
};

var cmdColor = cmdCode switch
{
    0x03 => "#22C55E",   // 上报 - 绿色
    0x05 or 0x07 or 0x08 => "#3478F6", // 回复 - 蓝色
    0x85 or 0x87 or 0x88 => "#FF9800", // 指令 - 橙色
    _ => "#EF5350"       // 未知 - 红色
};

var cmdSeverity = cmdCode switch
{
    0x03 or 0x05 or 0x07 or 0x08 or 0x85 or 0x87 or 0x88 => FieldSeverity.Normal,
    _ => FieldSeverity.Error
};

fields.Add(new FieldAnnotation
{
    Name = "指令码", Offset = offset, Length = 1, RawHex = RawHex(offset, 1),
    DisplayValue = $"0x{cmdCode:X2} ({cmdName})", Color = cmdColor, Severity = cmdSeverity
});
offset += 1;

if (offset >= RawData.Length) return fields;

// ===== 随机数(RN) =====
var rn = ToUInt16(offset);
fields.Add(new FieldAnnotation { Name = "随机数(RN)", Offset = offset, Length = 2, RawHex = RawHex(offset, 2), DisplayValue = $"0x{rn:X4}" });
offset += 2;

// ===== 根据指令码解析后续内容 =====
switch (cmdCode)
{
    case 0x03: // 心跳上报
        ParseHeartbeat(fields, offset, payloadLen);
        break;
    case 0x85: // 查询指令
        ParseQueryCmd(fields, offset, payloadLen);
        break;
    case 0x87: // 控制指令
        ParseCtrlCmd(fields, offset, payloadLen);
        break;
    case 0x88: // 修改指令
        ParseModifyCmd(fields, offset, payloadLen);
        break;
    case 0x05: // 查询回复
        ParseQueryResp(fields, offset, payloadLen);
        break;
    case 0x07: // 控制回复
        ParseCtrlResp(fields, offset, payloadLen);
        break;
    case 0x08: // 修改回复
        ParseModifyResp(fields, offset, payloadLen);
        break;
    default:
        if (payloadLen > 0)
            fields.Add(new FieldAnnotation { Name = "数据", Offset = offset, Length = payloadLen, RawHex = RawHex(offset, payloadLen), DisplayValue = $"{payloadLen} 字节", Color = "#888888" });
        break;
}

// ===== CRC16 (倒数第3~2字节) =====
int crcOffset = RawData.Length - 3;
if (crcOffset >= offset)
{
    var storedCrc = ToUInt16(crcOffset);
    var calcCrc = Crc16(0, crcOffset);
    bool crcOk = storedCrc == calcCrc;
    fields.Add(new FieldAnnotation
    {
        Name = "CRC16", Offset = crcOffset, Length = 2, RawHex = RawHex(crcOffset, 2),
        DisplayValue = crcOk ? $"0x{storedCrc:X4} (正确)" : $"0x{storedCrc:X4} (计算值=0x{calcCrc:X4})",
        Color = crcOk ? "#22C55E" : "#EF5350",
        Severity = crcOk ? FieldSeverity.Normal : FieldSeverity.Error
    });
}

// ===== 帧尾 =====
int footerOffset = RawData.Length - 1;
if (footerOffset > offset)
{
    bool footerOk = RawData[footerOffset] == 0xDD;
    fields.Add(new FieldAnnotation
    {
        Name = "帧尾", Offset = footerOffset, Length = 1, RawHex = RawHex(footerOffset, 1),
        DisplayValue = footerOk ? "DD (正确)" : $"0x{RawData[footerOffset]:X2} (错误,期望DD)",
        Color = footerOk ? "#3478F6" : "#EF5350",
        Severity = footerOk ? FieldSeverity.Normal : FieldSeverity.Error
    });
}

return fields;

// ===== 辅助函数 =====

void ParseHeartbeat(List<FieldAnnotation> f, int off, int len)
{
    int expected = 12;
    if (len < expected)
    {
        f.Add(new FieldAnnotation { Name = "数据(异常)", Offset = off, Length = len, RawHex = RawHex(off, len), DisplayValue = $"心跳数据长度不足{expected}字节", Color = "#EF5350", Severity = FieldSeverity.Error });
        return;
    }

    // Power [0-3] float
    var power = ToFloat(off);
    f.Add(new FieldAnnotation { Name = "功率(Power)", Offset = off, Length = 4, RawHex = RawHex(off, 4), DisplayValue = $"{power:F1}W", Color = "#FF9800" });
    off += 4;

    // Temperature [4-7] float
    var temp = ToFloat(off);
    f.Add(new FieldAnnotation { Name = "温度(Temp)", Offset = off, Length = 4, RawHex = RawHex(off, 4), DisplayValue = $"{temp:F1}°C", Color = "#FF9800" });
    off += 4;

    // Status [8]
    var status = RawData[off];
    f.Add(new FieldAnnotation { Name = "开关状态(Status)", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = status == 1 ? "ON" : "OFF", Color = status == 1 ? "#22C55E" : "#888888" });
    off++;

    // Mode [9]
    var mode = RawData[off];
    string modeName = mode switch { 0 => "Auto", 1 => "Cold", 2 => "Hot", 3 => "Dry", 4 => "Fan", 5 => "Sleep", _ => $"Unknown({mode})" };
    f.Add(new FieldAnnotation { Name = "模式(Mode)", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = $"{mode} ({modeName})", Color = mode <= 5 ? "#3478F6" : "#FF9800", Severity = mode <= 5 ? FieldSeverity.Normal : FieldSeverity.Warning });
    off++;

    // SetTemp [10]
    var setTemp = RawData[off];
    bool tempOk = setTemp >= 16 && setTemp <= 30;
    f.Add(new FieldAnnotation { Name = "设定温度(SetTemp)", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = $"{setTemp}°C", Color = tempOk ? "#3478F6" : "#FF9800", Severity = tempOk ? FieldSeverity.Normal : FieldSeverity.Warning });
    off++;

    // Wind [11]
    var wind = RawData[off];
    f.Add(new FieldAnnotation { Name = "风速(Wind)", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = $"{wind}" });
    off++;

    // Remaining
    int remaining = len - expected;
    if (remaining > 0)
        f.Add(new FieldAnnotation { Name = "附加数据", Offset = off, Length = remaining, RawHex = RawHex(off, remaining), DisplayValue = $"{remaining} 字节", Color = "#888888" });
}

void ParseSubCmd(List<FieldAnnotation> f, int off, string cmdType, string prefix)
{
    if (off >= RawData.Length) return;
    var sub = RawData[off];

    string subName = GetSubCmdName(cmdType, sub);
    var severity = subName.StartsWith("未知") ? FieldSeverity.Warning : FieldSeverity.Normal;

    f.Add(new FieldAnnotation
    {
        Name = $"{prefix}子命令", Offset = off, Length = 1, RawHex = RawHex(off, 1),
        DisplayValue = $"0x{sub:X2} ({subName})",
        Color = severity == FieldSeverity.Warning ? "#FF9800" : "#3478F6",
        Severity = severity
    });
    off++;

    int remaining = RawData.Length - off - 3; // excluding CRC(2) + DD(1)
    if (remaining > 0)
        f.Add(new FieldAnnotation { Name = $"{prefix}参数", Offset = off, Length = remaining, RawHex = RawHex(off, remaining), DisplayValue = $"{remaining} 字节", Color = "#888888" });
}

void ParseQueryCmd(List<FieldAnnotation> f, int off, int len) { ParseSubCmd(f, off, "query", "查询"); }
void ParseCtrlCmd(List<FieldAnnotation> f, int off, int len) { ParseSubCmd(f, off, "ctrl", "控制"); }
void ParseModifyCmd(List<FieldAnnotation> f, int off, int len) { ParseSubCmd(f, off, "modify", "修改"); }

void ParseCtrlResp(List<FieldAnnotation> f, int off, int len)
{
    // 帧: RN(2B) + 控制对象(1B) + 结果(1B) + 状态码(1B)
    if (len < 5)
    {
        f.Add(new FieldAnnotation { Name = "数据(异常)", Offset = off, Length = len, RawHex = RawHex(off, len), DisplayValue = "控制回复数据长度不足", Color = "#EF5350", Severity = FieldSeverity.Error });
        return;
    }

    // RN already parsed
    var ctrlObj = RawData[off];
    f.Add(new FieldAnnotation { Name = "控制对象", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = $"0x{ctrlObj:X2}" });
    off++;

    var result = RawData[off];
    var resultStr = result == 0 ? "成功" : "失败";
    f.Add(new FieldAnnotation { Name = "执行结果", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = resultStr, Color = result == 0 ? "#22C55E" : "#EF5350", Severity = result == 0 ? FieldSeverity.Normal : FieldSeverity.Error });
    off++;

    var status = RawData[off];
    string statusName = GetStatusName(status);
    var statusColor = status switch { 0x00 => "#22C55E", 0xFF => "#EF5350", _ => "#FF9800" };
    var statusSev = status switch { 0x00 => FieldSeverity.Normal, 0xFF => FieldSeverity.Error, _ => FieldSeverity.Warning };
    f.Add(new FieldAnnotation { Name = "状态码", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = $"0x{status:X2} ({statusName})", Color = statusColor, Severity = statusSev });
    off++;

    int remaining = len - 5;
    if (remaining > 0)
        f.Add(new FieldAnnotation { Name = "附加数据", Offset = off, Length = remaining, RawHex = RawHex(off, remaining), DisplayValue = $"{remaining} 字节", Color = "#888888" });
}

void ParseQueryResp(List<FieldAnnotation> f, int off, int len)
{
    // 帧: RN(2B) + 查询内容(1B) + 总包数(1B) + 分包号(1B) + 查询数据
    if (len < 5)
    {
        f.Add(new FieldAnnotation { Name = "数据(异常)", Offset = off, Length = len, RawHex = RawHex(off, len), DisplayValue = "查询回复数据长度不足", Color = "#EF5350", Severity = FieldSeverity.Error });
        return;
    }

    var queryContent = RawData[off];
    string qName = GetSubCmdName("query", queryContent);
    f.Add(new FieldAnnotation { Name = "查询内容", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = $"0x{queryContent:X2} ({qName})", Color = "#3478F6" });
    off++;

    var totalPkgs = RawData[off];
    f.Add(new FieldAnnotation { Name = "总包数", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = $"{totalPkgs}" });
    off++;

    var pkgNo = RawData[off];
    f.Add(new FieldAnnotation { Name = "分包号", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = $"{pkgNo}" });
    off++;

    int dataLen = len - 5;
    if (dataLen > 0)
        ParseQueryData(f, off, dataLen, queryContent);
}

void ParseQueryData(List<FieldAnnotation> f, int off, int dataLen, byte queryContent)
{
    switch (queryContent)
    {
        case 0x01: // 设备信息: 6B设备ID + 4B固件版本 + 4B硬件版本
            if (dataLen >= 14)
            {
                f.Add(new FieldAnnotation { Name = "设备ID", Offset = off, Length = 6, RawHex = RawHex(off, 6), DisplayValue = BitConverter.ToString(RawData, off, 6).Replace("-", ":"), Color = "#888888" });
                var fw = ToUInt32(off + 6);
                f.Add(new FieldAnnotation { Name = "固件版本", Offset = off + 6, Length = 4, RawHex = RawHex(off + 6, 4), DisplayValue = $"{fw}" });
                var hw = ToUInt32(off + 10);
                f.Add(new FieldAnnotation { Name = "硬件版本", Offset = off + 10, Length = 4, RawHex = RawHex(off + 10, 4), DisplayValue = $"{hw}" });
            }
            break;
        case 0x02: // 设备状态: 1B开关 + 1B模式 + 1B温度 + 1B风速 + 1B连接状态
            if (dataLen >= 5)
            {
                var sw = RawData[off];
                f.Add(new FieldAnnotation { Name = "开关", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = sw == 1 ? "ON" : "OFF" });
                var mode = RawData[off + 1];
                string modeName = mode switch { 0 => "Auto", 1 => "Cold", 2 => "Hot", 3 => "Dry", 4 => "Fan", 5 => "Sleep", _ => $"?({mode})" };
                f.Add(new FieldAnnotation { Name = "模式", Offset = off + 1, Length = 1, RawHex = RawHex(off + 1, 1), DisplayValue = modeName });
                f.Add(new FieldAnnotation { Name = "温度", Offset = off + 2, Length = 1, RawHex = RawHex(off + 2, 1), DisplayValue = $"{RawData[off + 2]}°C" });
                f.Add(new FieldAnnotation { Name = "风速", Offset = off + 3, Length = 1, RawHex = RawHex(off + 3, 1), DisplayValue = $"{RawData[off + 3]}" });
                var conn = RawData[off + 4];
                f.Add(new FieldAnnotation { Name = "连接状态", Offset = off + 4, Length = 1, RawHex = RawHex(off + 4, 1), DisplayValue = conn == 1 ? "已连接" : "未连接", Color = conn == 1 ? "#22C55E" : "#888888" });
            }
            break;
        case 0x03: // 功率: 4B float
            if (dataLen >= 4)
            {
                var pwr = ToFloat(off);
                f.Add(new FieldAnnotation { Name = "功率", Offset = off, Length = 4, RawHex = RawHex(off, 4), DisplayValue = $"{pwr:F1}W", Color = "#FF9800" });
            }
            break;
        case 0x04: // 温度: 4B float
            if (dataLen >= 4)
            {
                var tmp = ToFloat(off);
                f.Add(new FieldAnnotation { Name = "温度", Offset = off, Length = 4, RawHex = RawHex(off, 4), DisplayValue = $"{tmp:F1}°C", Color = "#FF9800" });
            }
            break;
        case 0x05: // ADC: 2B功率ADC + 2B NTC ADC
            if (dataLen >= 4)
            {
                f.Add(new FieldAnnotation { Name = "功率ADC", Offset = off, Length = 2, RawHex = RawHex(off, 2), DisplayValue = $"{ToUInt16(off)}" });
                f.Add(new FieldAnnotation { Name = "NTC ADC", Offset = off + 2, Length = 2, RawHex = RawHex(off + 2, 2), DisplayValue = $"{ToUInt16(off + 2)}" });
            }
            break;
        case 0x06: // BLE名: 1B长度 + N B
            if (dataLen >= 1)
            {
                var nameLen = RawData[off];
                var name = System.Text.Encoding.ASCII.GetString(RawData, off + 1, Math.Min(nameLen, dataLen - 1));
                f.Add(new FieldAnnotation { Name = "BLE名称", Offset = off, Length = 1 + nameLen, RawHex = RawHex(off, 1 + nameLen), DisplayValue = name, Color = "#888888" });
            }
            break;
        case 0x07: // 设备地址: 2B
            if (dataLen >= 2)
                f.Add(new FieldAnnotation { Name = "设备地址", Offset = off, Length = 2, RawHex = RawHex(off, 2), DisplayValue = $"{ToUInt16(off)}" });
            break;
        case 0x0C: // 查询设备ID
        case 0x0D: // 查询产品ID
        case 0x0E: // 查询设备KEY
        case 0x0F: // 查询平台类型
        case 0x10: // 查询域名端口
        {
            string label = queryContent switch { 0x0C => "设备ID", 0x0D => "产品ID", 0x0E => "设备KEY", 0x0F => "平台类型", 0x10 => "域名端口", _ => $"IoT参数(0x{queryContent:X2})" };
            var str = System.Text.Encoding.ASCII.GetString(RawData, off, dataLen).TrimEnd('\0');
            f.Add(new FieldAnnotation { Name = label, Offset = off, Length = dataLen, RawHex = RawHex(off, dataLen), DisplayValue = str, Color = "#888888" });
            break;
        }
        default:
            if (dataLen > 0)
                f.Add(new FieldAnnotation { Name = $"查询数据(0x{queryContent:X2})", Offset = off, Length = dataLen, RawHex = RawHex(off, dataLen), DisplayValue = $"{dataLen} 字节", Color = "#888888" });
            break;
    }
}

void ParseModifyResp(List<FieldAnnotation> f, int off, int len)
{
    // 帧: RN(2B) + 修改内容(1B) + 修改结果(1B)
    if (len < 4)
    {
        f.Add(new FieldAnnotation { Name = "数据(异常)", Offset = off, Length = len, RawHex = RawHex(off, len), DisplayValue = "修改回复数据长度不足", Color = "#EF5350", Severity = FieldSeverity.Error });
        return;
    }

    var modContent = RawData[off];
    string modName = GetSubCmdName("modify", modContent);
    f.Add(new FieldAnnotation { Name = "修改内容", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = $"0x{modContent:X2} ({modName})", Color = "#3478F6" });
    off++;

    var modResult = RawData[off];
    bool modOk = modResult == 0x00;
    f.Add(new FieldAnnotation { Name = "修改结果", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = modOk ? "成功" : "失败", Color = modOk ? "#22C55E" : "#EF5350", Severity = modOk ? FieldSeverity.Normal : FieldSeverity.Error });
    off++;

    int remaining = len - 4;
    if (remaining > 0)
        f.Add(new FieldAnnotation { Name = "附加数据", Offset = off, Length = remaining, RawHex = RawHex(off, remaining), DisplayValue = $"{remaining} 字节", Color = "#888888" });
}

// ===== 数据查询辅助 =====

string GetSubCmdName(string cmdType, byte sub)
{
    return (cmdType, sub) switch
    {
        ("query", 0x01) => "查询设备信息",
        ("query", 0x02) => "查询设备状态",
        ("query", 0x03) => "查询功率",
        ("query", 0x04) => "查询温度",
        ("query", 0x05) => "查询ADC",
        ("query", 0x06) => "查询BLE名称",
        ("query", 0x07) => "查询设备地址",
        ("query", 0x08) => "查询定时状态",
        ("query", 0x09) => "查询MQTT配置",
        ("query", 0x0A) => "查询监听状态",
        ("query", 0x0B) => "查询红外数据",
        ("query", 0x0C) => "查询设备ID",
        ("query", 0x0D) => "查询产品ID",
        ("query", 0x0E) => "查询设备KEY",
        ("query", 0x0F) => "查询平台类型",
        ("query", 0x10) => "查询域名端口",

        ("ctrl", 0x01) => "开关控制",
        ("ctrl", 0x02) => "模式控制",
        ("ctrl", 0x03) => "温度设置",
        ("ctrl", 0x04) => "风速设置",
        ("ctrl", 0x05) => "温度增加(+1)",
        ("ctrl", 0x06) => "温度减少(-1)",
        ("ctrl", 0x07) => "红外学习开始",
        ("ctrl", 0x08) => "红外学习停止",
        ("ctrl", 0x09) => "红外发送",
        ("ctrl", 0x0A) => "保存红外按键",
        ("ctrl", 0x0B) => "监听总开关",
        ("ctrl", 0x0C) => "切换监听模式",
        ("ctrl", 0x0D) => "时间同步",
        ("ctrl", 0x0E) => "设置BLE名称",
        ("ctrl", 0x0F) => "设置设备地址",
        ("ctrl", 0x10) => "设置定时关",
        ("ctrl", 0x11) => "设置定时参数",
        ("ctrl", 0x12) => "设置MQTT配置(TLV)",

        ("modify", 0x12) => "修改设备时间",
        ("modify", 0x13) => "修改设备ID(分包)",
        ("modify", 0x14) => "修改产品ID(分包)",
        ("modify", 0x15) => "修改设备KEY(分包)",
        ("modify", 0x18) => "修改平台类型(分包)",
        ("modify", 0x19) => "修改域名端口(分包)",

        _ => $"未知(0x{sub:X2})"
    };
}

string GetStatusName(byte status)
{
    return status switch
    {
        0x00 => "SUCCESS",
        0x01 => "ERROR_PARAM",
        0x02 => "ERROR_CMD",
        0x03 => "ERROR_CRC",
        0x04 => "ERROR_BUSY",
        0x05 => "ERROR_STORAGE",
        0x10 => "LISTEN_OFF",
        0x11 => "LEARN_MODE",
        0x12 => "NO_LEARN_DATA",
        0xFF => "ERROR_FAIL",
        _ => $"UNKNOWN(0x{status:X2})"
    };
}

static uint ToUInt32(int offset)
{
    if (offset + 4 > RawData.Length) return 0;
    return (uint)(RawData[offset] | (RawData[offset + 1] << 8) | (RawData[offset + 2] << 16) | (RawData[offset + 3] << 24));
}
```

- [ ] **Step 2: Verify the parser compiles**

The parser uses C# Script (.csx) compiled at runtime by Roslyn. We need to verify it compiles:

```bash
# Build the project to ensure the parser file is syntactically valid
dotnet build src/ACCcom.Core/ACCcom.Core.csproj
```

- [ ] **Step 3: Test with sample frames**

Create a test using the `ParseRaw` MCP tool or the WPF app's parse-raw feature with known frame structures:

Test heartbeat frame: `A5 5A 11 07 0B 03 RN(2B) + Power(4B) + Temp(4B) + Status(1B) + Mode(1B) + SetTemp(1B) + Wind(1B) + CRC16(2B) DD`

Test query frame: `A5 5A 08 07 04 85 RN(2B) 02 CRC16(2B) DD` (query device status)

- [ ] **Step 4: Commit**

```bash
git add src/ACCcom/parsers/esoac_v3.csx docs/superpowers/plans/2026-06-11-esoac-v3-parser.md
git commit -m "feat: add ESOAC V3 protocol parser"
```

---

## Self-Review

**Spec coverage check:**
- ✅ Frame header detection (A5 5A)
- ✅ Content type (0x07) validation
- ✅ CRC16 validation
- ✅ Frame footer (DD) validation
- ✅ All 7 command codes (0x03, 0x05, 0x07, 0x08, 0x85, 0x87, 0x88)
- ✅ Heartbeat (0x03) full field parsing with Power/Temp/Status/Mode/SetTemp/Wind
- ✅ Query cmd (0x85) with 16 sub-commands
- ✅ Ctrl cmd (0x87) with 18 sub-commands
- ✅ Modify cmd (0x88) with 6 sub-commands
- ✅ Ctrl Response (0x07) with status codes
- ✅ Query Response (0x05) with data parsing for 0x01~0x07, 0x0C~0x10
- ✅ Modify Response (0x08) with success/fail
- ✅ Status code mapping (0x00~0x05, 0x10~0x12, 0xFF)
- ✅ Error handling for short frames, unknown commands, CRC failure

**Placeholder scan:** No placeholders found.

**Type consistency:** All function signatures consistent.
