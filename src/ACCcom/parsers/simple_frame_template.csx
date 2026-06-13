// 简单帧协议解析模板
// 帧结构: [帧头(2)] [长度(1)] [命令(1)] [数据(N)] [校验(1)]
// 使用方法: 修改帧头、命令定义以匹配你的设备

var result = new List<FieldAnnotation>();

if (RawData.Length < 5)
{
    result.Add(new FieldAnnotation { Name = "错误", Offset = 0, Length = RawData.Length, RawHex = RawHex(0, RawData.Length), DisplayValue = "帧长度不足5字节", Color = "#EF5350", Severity = FieldSeverity.Error });
    return result;
}

// 帧头
var header = ToUInt16(0, true);
bool isValidHeader = header == 0xAA55;
result.Add(new FieldAnnotation {
    Name = "帧头", Offset = 0, Length = 2,
    RawHex = RawHex(0, 2),
    DisplayValue = isValidHeader ? $"0x{header:X4} (正确)" : $"0x{header:X4} (期望AA55)",
    Color = isValidHeader ? "#3478F6" : "#EF5350",
    Severity = isValidHeader ? FieldSeverity.Normal : FieldSeverity.Error
});

// 长度
var dataLen = RawData[2];
bool lenOk = dataLen == RawData.Length - 5;
result.Add(new FieldAnnotation {
    Name = "数据长度", Offset = 2, Length = 1,
    RawHex = RawHex(2, 1),
    DisplayValue = $"{dataLen} 字节" + (lenOk ? "" : $" (帧实际剩余{RawData.Length - 5}字节)"),
    Color = lenOk ? "#3478F6" : "#FF9800",
    Severity = lenOk ? FieldSeverity.Normal : FieldSeverity.Warning
});

// 命令
var cmd = RawData[3];
string cmdName = cmd switch
{
    0x01 => "查询状态",
    0x02 => "读取数据",
    0x03 => "写入数据",
    0x04 => "复位设备",
    0x05 => "开始测量",
    0x06 => "停止测量",
    0x81 => "状态回复",
    0x82 => "数据回复",
    0x83 => "写入回复",
    0x84 => "复位回复",
    0x85 => "测量回复",
    _ => cmd >= 0x80 ? $"响应(0x{cmd:X2})" : $"请求(0x{cmd:X2})"
};
result.Add(new FieldAnnotation {
    Name = "命令", Offset = 3, Length = 1,
    RawHex = RawHex(3, 1),
    DisplayValue = $"0x{cmd:X2} ({cmdName})",
    Color = cmd >= 0x80 ? "#22C55E" : "#3478F6",
    Severity = FieldSeverity.Normal
});

// 数据区
if (dataLen > 0 && RawData.Length >= 4 + dataLen)
{
    result.Add(new FieldAnnotation {
        Name = "数据区", Offset = 4, Length = dataLen,
        RawHex = RawHex(4, dataLen),
        DisplayValue = $"{dataLen} 字节",
        Color = "#888888",
        Severity = FieldSeverity.Normal
    });
}

// 校验
var checksumOffset = 4 + dataLen;
if (checksumOffset < RawData.Length)
{
    var received = RawData[checksumOffset];
    var calc = Xor8(0, checksumOffset);
    bool csOk = received == calc;
    result.Add(new FieldAnnotation {
        Name = "校验(XOR)", Offset = checksumOffset, Length = 1,
        RawHex = RawHex(checksumOffset, 1),
        DisplayValue = csOk ? $"0x{received:X2} (校验通过)" : $"0x{received:X2} (计算值=0x{calc:X2})",
        Color = csOk ? "#22C55E" : "#EF5350",
        Severity = csOk ? FieldSeverity.Normal : FieldSeverity.Error
    });
}

return result;
