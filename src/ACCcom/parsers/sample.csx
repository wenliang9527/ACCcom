// sample.csx - 自定义温控仪协议示例
// 帧结构: [帧头AA55(2)] [长度(1)] [命令(1)] [温度(1)] [校验(1)]
// 使用方法: 复制此文件，修改帧结构和字段定义以匹配你的设备

var result = new List<FieldAnnotation>();

if (RawData.Length < 5)
{
    result.Add(new FieldAnnotation { Name = "错误", Offset = 0, Length = RawData.Length, RawHex = RawHex(0, RawData.Length), DisplayValue = "帧长度不足5字节", Color = "#EF5350", Severity = FieldSeverity.Error });
    return result;
}

// 帧头
var header = ToUInt16(0, true);
bool headerOk = header == 0xAA55;
result.Add(new FieldAnnotation
{
    Name = "帧头", Offset = 0, Length = 2,
    RawHex = RawHex(0, 2),
    DisplayValue = headerOk ? "AA 55 (正确)" : $"0x{header:X4} (期望AA55)",
    Color = headerOk ? "#3478F6" : "#EF5350",
    Severity = headerOk ? FieldSeverity.Normal : FieldSeverity.Error
});

// 长度
var dataLen = RawData[2];
result.Add(new FieldAnnotation
{
    Name = "长度", Offset = 2, Length = 1,
    RawHex = RawHex(2, 1),
    DisplayValue = $"{dataLen} 字节",
    Color = "#3478F6"
});

// 命令
var cmd = RawData[3];
string cmdName = cmd switch
{
    0x01 => "读取温度",
    0x02 => "设置目标温度",
    0x03 => "读取状态",
    0x04 => "启动加热",
    0x05 => "停止加热",
    0x81 => "温度回复",
    0x82 => "设置回复",
    0x83 => "状态回复",
    _ => cmd >= 0x80 ? $"响应(0x{cmd:X2})" : $"请求(0x{cmd:X2})"
};
result.Add(new FieldAnnotation
{
    Name = "命令", Offset = 3, Length = 1,
    RawHex = RawHex(3, 1),
    DisplayValue = $"0x{cmd:X2} ({cmdName})",
    Color = cmd >= 0x80 ? "#22C55E" : "#3478F6"
});

// 温度
var temp = RawData[4];
string tempColor = temp > 80 ? "#EF5350" : temp > 60 ? "#FF9800" : "#22C55E";
var tempSev = temp > 80 ? FieldSeverity.Error : temp > 60 ? FieldSeverity.Warning : FieldSeverity.Normal;
result.Add(new FieldAnnotation
{
    Name = "温度", Offset = 4, Length = 1,
    RawHex = RawHex(4, 1),
    DisplayValue = $"{temp} °C",
    Color = tempColor,
    Severity = tempSev
});

// 校验(如果有)
if (RawData.Length >= 6)
{
    var received = RawData[5];
    var calc = Xor8(0, 5);
    bool csOk = received == calc;
    result.Add(new FieldAnnotation
    {
        Name = "校验", Offset = 5, Length = 1,
        RawHex = RawHex(5, 1),
        DisplayValue = csOk ? $"0x{received:X2} (校验通过)" : $"0x{received:X2} (计算值=0x{calc:X2})",
        Color = csOk ? "#22C55E" : "#EF5350",
        Severity = csOk ? FieldSeverity.Normal : FieldSeverity.Error
    });
}

return result;
