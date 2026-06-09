// sample.csx
// 协议：自定义温控仪
// 帧头: AA 55, 第3字节=长度, 末字节=CRC16低8位

var result = new List<FieldAnnotation>();

if (RawData.Length < 5) return result;

result.Add(new FieldAnnotation
{
    Name = "帧头",
    Offset = 0,
    Length = 2,
    RawHex = RawHex(0, 2),
    DisplayValue = "AA 55",
    Color = "#888888"
});

result.Add(new FieldAnnotation
{
    Name = "长度",
    Offset = 2,
    Length = 1,
    RawHex = RawHex(2, 1),
    DisplayValue = $"{RawData[2]} 字节"
});

result.Add(new FieldAnnotation
{
    Name = "温度",
    Offset = 4,
    Length = 1,
    RawHex = RawHex(4, 1),
    DisplayValue = $"{RawData[4]} °C",
    Severity = RawData[4] > 80 ? FieldSeverity.Warning : FieldSeverity.Normal
});

return result;
