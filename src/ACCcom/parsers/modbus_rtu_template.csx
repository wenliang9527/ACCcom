// Modbus RTU 协议解析模板
// 帧结构: [设备地址(1)] [功能码(1)] [数据(N)] [CRC16(2)]
// 使用方法: 修改下方的字段映射逻辑以匹配你的设备

var result = new List<FieldAnnotation>();

if (RawData.Length < 4)
{
    result.Add(new FieldAnnotation { Name = "错误", Offset = 0, Length = RawData.Length, RawHex = RawHex(0, RawData.Length), DisplayValue = "帧长度不足4字节(Min: addr+func+CRC)", Color = "#EF5350", Severity = FieldSeverity.Error });
    return result;
}

// 设备地址
var addr = RawData[0];
string addrDesc = addr == 0 ? "广播地址(所有设备)" : addr == 0xFF ? "保留地址" : $"设备 #{addr}";
result.Add(new FieldAnnotation {
    Name = "设备地址", Offset = 0, Length = 1,
    RawHex = RawHex(0, 1),
    DisplayValue = $"0x{addr:X2} ({addrDesc})",
    Color = addr == 0 ? "#FF9800" : "#3478F6",
    Severity = FieldSeverity.Normal
});

// 功能码
var funcCode = RawData[1];
bool isException = funcCode > 0x80;
var funcName = funcCode switch
{
    0x01 => "读线圈状态(Read Coils)",
    0x02 => "读离散输入(Read Discrete Inputs)",
    0x03 => "读保持寄存器(Read Holding Registers)",
    0x04 => "读输入寄存器(Read Input Registers)",
    0x05 => "写单个线圈(Write Single Coil)",
    0x06 => "写单个寄存器(Write Single Register)",
    0x0F => "写多个线圈(Write Multiple Coils)",
    0x10 => "写多个寄存器(Write Multiple Registers)",
    0x17 => "读/写多个寄存器(Read/Write Multiple)",
    0x83 => "异常:读保持寄存器",
    0x84 => "异常:读输入寄存器",
    0x86 => "异常:写单个寄存器",
    0x90 => "异常:写多个寄存器",
    _ => isException ? $"异常功能码(0x{funcCode:X2})" : $"未知(0x{funcCode:X2})"
};
result.Add(new FieldAnnotation {
    Name = "功能码", Offset = 1, Length = 1,
    RawHex = RawHex(1, 1),
    DisplayValue = $"0x{funcCode:X2} ({funcName})",
    Color = isException ? "#EF5350" : "#3478F6",
    Severity = isException ? FieldSeverity.Error : FieldSeverity.Normal
});

// CRC 校验
var receivedCrc = ToUInt16(RawData.Length - 2, false);
var calcCrc = Crc16(0, RawData.Length - 2);
bool crcOk = receivedCrc == calcCrc;
result.Add(new FieldAnnotation {
    Name = "CRC16", Offset = RawData.Length - 2, Length = 2,
    RawHex = RawHex(RawData.Length - 2, 2),
    DisplayValue = crcOk ? $"0x{receivedCrc:X4} (校验通过)" : $"0x{receivedCrc:X4} (计算值=0x{calcCrc:X4})",
    Color = crcOk ? "#22C55E" : "#EF5350",
    Severity = crcOk ? FieldSeverity.Normal : FieldSeverity.Error
});

// 数据区语义解析
int dataStart = 2;
int dataLen = RawData.Length - 4;

if (isException && dataLen >= 1)
{
    var exCode = RawData[dataStart];
    string exName = exCode switch
    {
        0x01 => "非法功能码(Illegal Function)",
        0x02 => "非法数据地址(Illegal Data Address)",
        0x03 => "非法数据值(Illegal Data Value)",
        0x04 => "从站设备故障(Server Device Failure)",
        0x05 => "确认(Acknowledge)",
        0x06 => "从站设备忙(Server Device Busy)",
        0x08 => "存储奇偶性差错(Memory Parity Error)",
        0x0A => "不可用网关路径(Gateway Path Unavailable)",
        0x0B => "网关目标设备响应失败(Gateway Target Device Failed)",
        _ => $"未知异常(0x{exCode:X2})"
    };
    result.Add(new FieldAnnotation { Name = "异常码", Offset = dataStart, Length = 1, RawHex = RawHex(dataStart, 1), DisplayValue = $"0x{exCode:X2} ({exName})", Color = "#EF5350", Severity = FieldSeverity.Error });
}
else if (!isException)
{
    switch (funcCode)
    {
        case 0x01:
        case 0x02:
            if (dataLen >= 1)
            {
                var byteCount = RawData[dataStart];
                result.Add(new FieldAnnotation { Name = "字节数", Offset = dataStart, Length = 1, RawHex = RawHex(dataStart, 1), DisplayValue = $"{byteCount} 字节", Color = "#3478F6" });
                if (dataLen >= 1 + byteCount)
                {
                    var coils = new System.Collections.Generic.List<string>();
                    for (int i = 0; i < byteCount; i++)
                    {
                        var b = RawData[dataStart + 1 + i];
                        for (int bit = 0; bit < 8; bit++)
                            coils.Add((b & (1 << bit)) != 0 ? "ON" : "OFF");
                    }
                    result.Add(new FieldAnnotation { Name = "线圈值", Offset = dataStart + 1, Length = byteCount, RawHex = RawHex(dataStart + 1, byteCount), DisplayValue = string.Join(",", coils), Color = "#22C55E" });
                }
            }
            break;
        case 0x03:
        case 0x04:
            if (dataLen >= 1)
            {
                var byteCount = RawData[dataStart];
                result.Add(new FieldAnnotation { Name = "字节数", Offset = dataStart, Length = 1, RawHex = RawHex(dataStart, 1), DisplayValue = $"{byteCount} 字节 ({byteCount / 2} 个寄存器)", Color = "#3478F6" });
                if (dataLen >= 1 + byteCount)
                {
                    int regCount = byteCount / 2;
                    for (int i = 0; i < regCount; i++)
                    {
                        var regVal = ToUInt16(dataStart + 1 + i * 2);
                        result.Add(new FieldAnnotation { Name = $"寄存器[{i}]", Offset = dataStart + 1 + i * 2, Length = 2, RawHex = RawHex(dataStart + 1 + i * 2, 2), DisplayValue = $"0x{regVal:X4} ({regVal})", Color = "#3478F6" });
                    }
                }
            }
            break;
        case 0x05:
            if (dataLen >= 4)
            {
                var regAddr = ToUInt16(dataStart);
                var regVal = ToUInt16(dataStart + 2);
                result.Add(new FieldAnnotation { Name = "线圈地址", Offset = dataStart, Length = 2, RawHex = RawHex(dataStart, 2), DisplayValue = $"0x{regAddr:X4} ({regAddr})", Color = "#3478F6" });
                result.Add(new FieldAnnotation { Name = "线圈值", Offset = dataStart + 2, Length = 2, RawHex = RawHex(dataStart + 2, 2), DisplayValue = regVal == 0xFF00 ? "ON (0xFF00)" : regVal == 0x0000 ? "OFF (0x0000)" : $"0x{regVal:X4}", Color = regVal == 0xFF00 ? "#22C55E" : "#888888" });
            }
            break;
        case 0x06:
            if (dataLen >= 4)
            {
                var regAddr = ToUInt16(dataStart);
                var regVal = ToUInt16(dataStart + 2);
                result.Add(new FieldAnnotation { Name = "寄存器地址", Offset = dataStart, Length = 2, RawHex = RawHex(dataStart, 2), DisplayValue = $"0x{regAddr:X4} ({regAddr})", Color = "#3478F6" });
                result.Add(new FieldAnnotation { Name = "写入值", Offset = dataStart + 2, Length = 2, RawHex = RawHex(dataStart + 2, 2), DisplayValue = $"0x{regVal:X4} ({regVal})", Color = "#22C55E" });
            }
            break;
        case 0x10:
            if (dataLen >= 4)
            {
                var startAddr = ToUInt16(dataStart);
                var quantity = ToUInt16(dataStart + 2);
                result.Add(new FieldAnnotation { Name = "起始地址", Offset = dataStart, Length = 2, RawHex = RawHex(dataStart, 2), DisplayValue = $"0x{startAddr:X4} ({startAddr})", Color = "#3478F6" });
                result.Add(new FieldAnnotation { Name = "寄存器数量", Offset = dataStart + 2, Length = 2, RawHex = RawHex(dataStart + 2, 2), DisplayValue = $"{quantity} 个", Color = "#3478F6" });
            }
            break;
        default:
            if (dataLen > 0)
                result.Add(new FieldAnnotation { Name = "数据区", Offset = dataStart, Length = dataLen, RawHex = RawHex(dataStart, dataLen), DisplayValue = $"{dataLen} 字节", Color = "#888888" });
            break;
    }
}

return result;
