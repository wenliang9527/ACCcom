// 迪瑞生化分析仪协议解析器
// 帧结构: Header(0xA55A) + DataLen(2) + Command(2) + DataChecksum(2) + HeaderChecksum(2) + Data(var)

var fields = new List<FieldAnnotation>();

if (RawData.Length < 10)
{
    fields.Add(new FieldAnnotation { Name = "错误", Offset = 0, Length = RawData.Length, RawHex = RawHex(0, RawData.Length), DisplayValue = "帧长度不足10字节", Color = "#EF5350", Severity = FieldSeverity.Error });
    return fields;
}

// --- 包头解析 ---
var header = ToUInt16(0, true);
fields.Add(new FieldAnnotation { Name = "帧头", Offset = 0, Length = 2, RawHex = RawHex(0, 2), DisplayValue = $"0x{header:X4}", Color = header == 0xA55A ? "#3478F6" : "#EF5350", Severity = header == 0xA55A ? FieldSeverity.Normal : FieldSeverity.Error });

var dataLen = ToUInt16(2);
fields.Add(new FieldAnnotation { Name = "数据长度", Offset = 2, Length = 2, RawHex = RawHex(2, 2), DisplayValue = $"{dataLen} 字节" });

var cmd = ToUInt16(4);
fields.Add(new FieldAnnotation { Name = "命令号", Offset = 4, Length = 2, RawHex = RawHex(4, 2), DisplayValue = $"0x{cmd:X4}", Color = "#3478F6" });

var dataChecksum = ToUInt16(6);
fields.Add(new FieldAnnotation { Name = "数据校验和", Offset = 6, Length = 2, RawHex = RawHex(6, 2), DisplayValue = $"0x{dataChecksum:X4}" });

var headerChecksum = ToUInt16(8);
fields.Add(new FieldAnnotation { Name = "包头校验和", Offset = 8, Length = 2, RawHex = RawHex(8, 2), DisplayValue = $"0x{headerChecksum:X4}" });

// 校验包头: 0x0100 + DataLen + Command + DataChecksum 应等于 HeaderChecksum
var calcHCS = (ushort)(0x0100 + dataLen + cmd + dataChecksum);
fields.Add(new FieldAnnotation
{
    Name = "包头校验",
    Offset = 8, Length = 2,
    RawHex = RawHex(8, 2),
    DisplayValue = calcHCS == headerChecksum ? "通过" : $"失败(期望0x{calcHCS:X4})",
    Color = calcHCS == headerChecksum ? "#22C55E" : "#EF5350",
    Severity = calcHCS == headerChecksum ? FieldSeverity.Normal : FieldSeverity.Error
});

// 数据区起始偏移
int dataOffset = 10;
int dataLenActual = Math.Min(dataLen, RawData.Length - dataOffset);

// 校验数据区
if (dataLenActual > 0)
{
    var calcDCS = Sum8(dataOffset, dataLenActual);
    fields.Add(new FieldAnnotation
    {
        Name = "数据校验",
        Offset = 6, Length = 2,
        RawHex = RawHex(6, 2),
        DisplayValue = calcDCS == (byte)(dataChecksum & 0xFF) ? "通过" : $"失败(期望0x{calcDCS:X2})",
        Color = calcDCS == (byte)(dataChecksum & 0xFF) ? "#22C55E" : "#EF5350",
        Severity = calcDCS == (byte)(dataChecksum & 0xFF) ? FieldSeverity.Normal : FieldSeverity.Warning
    });
}

// --- 命令名称映射 ---
string cmdName = cmd switch
{
    0x004A => "联机/脱机",
    0x004B => "启动测试(时间同步)",
    0x004C => "报警信息",
    0x004D => "测试状态",
    0x0001 => "开始复位",
    0x0002 => "复位完成",
    0x0003 => "紧急停止",
    0x0004 => "紧急停止完成",
    0x0005 => "机构检查",
    0x0006 => "机构检查完成",
    0x0007 => "光能量检查",
    0x0008 => "光能量检查完成",
    0x0009 => "杯空白测试",
    0x000A => "杯空白测试完成",
    0x000B => "光电数据上传",
    0x000C => "多试剂位信息",
    0x000D => "试剂交叉污染信息",
    0x000E => "常规项目测试",
    0x0010 => "项目测试完成",
    0x0011 => "样本条码扫描",
    0x0012 => "样本条码扫描完成",
    0x0013 => "试剂条码扫描",
    0x0014 => "试剂条码扫描完成",
    0x0015 => "试剂余量检测",
    0x0016 => "试剂余量检测完成",
    _ => $"未知命令"
};
fields.Add(new FieldAnnotation { Name = "命令名称", Offset = 4, Length = 2, RawHex = RawHex(4, 2), DisplayValue = cmdName, Color = "#3478F6" });

if (dataLenActual <= 0) return fields;

// --- 命令数据解析 ---
switch (cmd)
{
    case 0x004A: // 联机/脱机
    {
        var val = RawData[dataOffset];
        fields.Add(new FieldAnnotation { Name = "联机状态", Offset = dataOffset, Length = 1, RawHex = RawHex(dataOffset, 1), DisplayValue = val == 1 ? "联机" : "脱机", Color = val == 1 ? "#22C55E" : "#EF5350" });
        break;
    }
    case 0x004B: // 启动测试(时间同步)
    {
        if (dataLenActual >= 4)
        {
            fields.Add(new FieldAnnotation { Name = "时间-毫秒", Offset = dataOffset, Length = 1, RawHex = RawHex(dataOffset, 1), DisplayValue = $"{RawData[dataOffset]}" });
            fields.Add(new FieldAnnotation { Name = "时间-秒", Offset = dataOffset + 1, Length = 1, RawHex = RawHex(dataOffset + 1, 1), DisplayValue = $"{RawData[dataOffset + 1]}" });
            fields.Add(new FieldAnnotation { Name = "时间-分", Offset = dataOffset + 2, Length = 1, RawHex = RawHex(dataOffset + 2, 1), DisplayValue = $"{RawData[dataOffset + 2]}" });
            fields.Add(new FieldAnnotation { Name = "时间-小时", Offset = dataOffset + 3, Length = 1, RawHex = RawHex(dataOffset + 3, 1), DisplayValue = $"{RawData[dataOffset + 3]}" });
        }
        break;
    }
    case 0x004C: // 报警信息
    {
        if (dataLenActual >= 2)
        {
            fields.Add(new FieldAnnotation { Name = "主报警码(单元)", Offset = dataOffset, Length = 1, RawHex = RawHex(dataOffset, 1), DisplayValue = $"0x{RawData[dataOffset]:X2}", Color = "#FF9800", Severity = FieldSeverity.Warning });
            fields.Add(new FieldAnnotation { Name = "次报警码(部位)", Offset = dataOffset + 1, Length = 1, RawHex = RawHex(dataOffset + 1, 1), DisplayValue = $"0x{RawData[dataOffset + 1]:X2}", Color = "#FF9800", Severity = FieldSeverity.Warning });
        }
        break;
    }
    case 0x004D: // 测试状态
    {
        if (dataLenActual >= 9)
        {
            fields.Add(new FieldAnnotation { Name = "工作状态", Offset = dataOffset, Length = 1, RawHex = RawHex(dataOffset, 1), DisplayValue = $"0x{RawData[dataOffset]:X2}" });
            fields.Add(new FieldAnnotation { Name = "测试状态", Offset = dataOffset + 1, Length = 1, RawHex = RawHex(dataOffset + 1, 1), DisplayValue = $"0x{RawData[dataOffset + 1]:X2}" });
            fields.Add(new FieldAnnotation { Name = "环境温度", Offset = dataOffset + 2, Length = 1, RawHex = RawHex(dataOffset + 2, 1), DisplayValue = $"{RawData[dataOffset + 2]}°C" });
            fields.Add(new FieldAnnotation { Name = "制冷温度", Offset = dataOffset + 3, Length = 1, RawHex = RawHex(dataOffset + 3, 1), DisplayValue = $"{RawData[dataOffset + 3]}°C" });
            fields.Add(new FieldAnnotation { Name = "其它状态", Offset = dataOffset + 4, Length = 1, RawHex = RawHex(dataOffset + 4, 1), DisplayValue = $"0x{RawData[dataOffset + 4]:X2}" });
            var tempRaw = ToUInt16(dataOffset + 5);
            var tempRaw2 = ToUInt16(dataOffset + 7);
            fields.Add(new FieldAnnotation { Name = "恒温槽温度", Offset = dataOffset + 5, Length = 4, RawHex = RawHex(dataOffset + 5, 4), DisplayValue = $"{tempRaw}.{tempRaw2}" });
        }
        break;
    }
    case 0x0002: // 复位完成
    case 0x0004: // 紧急停止完成
    {
        var val = RawData[dataOffset];
        var label = cmd == 0x0002 ? "复位" : "停止";
        fields.Add(new FieldAnnotation { Name = $"{label}结果", Offset = dataOffset, Length = 1, RawHex = RawHex(dataOffset, 1), DisplayValue = val == 1 ? "成功" : "失败", Color = val == 1 ? "#22C55E" : "#EF5350", Severity = val == 1 ? FieldSeverity.Normal : FieldSeverity.Error });
        break;
    }
    case 0x0005: // 机构检查
    {
        if (dataLenActual >= 2)
        {
            var count = ToUInt16(dataOffset);
            fields.Add(new FieldAnnotation { Name = "检查次数", Offset = dataOffset, Length = 2, RawHex = RawHex(dataOffset, 2), DisplayValue = $"{count}" });
        }
        break;
    }
    case 0x000B: // 光电数据上传
    {
        if (dataLenActual >= 37)
        {
            for (int i = 0; i < 12; i++)
            {
                var val = ToUInt16(dataOffset + i * 2);
                fields.Add(new FieldAnnotation { Name = $"第{i + 1}路光电", Offset = dataOffset + i * 2, Length = 2, RawHex = RawHex(dataOffset + i * 2, 2), DisplayValue = $"{val}" });
            }
            int tsOff = dataOffset + 24;
            fields.Add(new FieldAnnotation { Name = "时间-小时", Offset = tsOff, Length = 1, RawHex = RawHex(tsOff, 1), DisplayValue = $"{RawData[tsOff]}" });
            fields.Add(new FieldAnnotation { Name = "时间-分", Offset = tsOff + 1, Length = 1, RawHex = RawHex(tsOff + 1, 1), DisplayValue = $"{RawData[tsOff + 1]}" });
            fields.Add(new FieldAnnotation { Name = "时间-秒", Offset = tsOff + 2, Length = 1, RawHex = RawHex(tsOff + 2, 1), DisplayValue = $"{RawData[tsOff + 2]}" });
            fields.Add(new FieldAnnotation { Name = "时间-毫秒", Offset = tsOff + 3, Length = 2, RawHex = RawHex(tsOff + 3, 2), DisplayValue = $"{ToUInt16(tsOff + 3)}" });
            var projId = (uint)(ToUInt16(dataOffset + 29) | (ToUInt16(dataOffset + 31) << 16));
            fields.Add(new FieldAnnotation { Name = "项目编号", Offset = dataOffset + 29, Length = 4, RawHex = RawHex(dataOffset + 29, 4), DisplayValue = $"{projId}" });
            var cupNo = ToUInt16(dataOffset + 33);
            fields.Add(new FieldAnnotation { Name = "反应杯号", Offset = dataOffset + 33, Length = 2, RawHex = RawHex(dataOffset + 33, 2), DisplayValue = $"{cupNo}" });
            fields.Add(new FieldAnnotation { Name = "光电测试次数", Offset = dataOffset + 35, Length = 1, RawHex = RawHex(dataOffset + 35, 1), DisplayValue = $"{RawData[dataOffset + 35]}" });
            var dataType = RawData[dataOffset + 36];
            string dtName = dataType switch
            {
                1 => "测试光电",
                2 => "杯空白光电",
                3 => "光量测试光电",
                4 => "空白光电",
                5 => "未使用杯子光电",
                6 => "光电稳定性",
                7 => "试剂空白",
                255 => "脏杯光电",
                _ => $"未知({dataType})"
            };
            fields.Add(new FieldAnnotation { Name = "光电类型", Offset = dataOffset + 36, Length = 1, RawHex = RawHex(dataOffset + 36, 1), DisplayValue = dtName, Color = dataType == 255 ? "#EF5350" : null });
        }
        break;
    }
    case 0x000D: // 试剂交叉污染信息
    {
        if (dataLenActual >= 5)
        {
            fields.Add(new FieldAnnotation { Name = "项目1编号", Offset = dataOffset, Length = 1, RawHex = RawHex(dataOffset, 1), DisplayValue = $"{RawData[dataOffset]}" });
            fields.Add(new FieldAnnotation { Name = "项目2编号", Offset = dataOffset + 1, Length = 1, RawHex = RawHex(dataOffset + 1, 1), DisplayValue = $"{RawData[dataOffset + 1]}" });
            fields.Add(new FieldAnnotation { Name = "清洗液编号", Offset = dataOffset + 2, Length = 1, RawHex = RawHex(dataOffset + 2, 1), DisplayValue = $"{RawData[dataOffset + 2]}" });
            var washVol = ToUInt16(dataOffset + 3);
            fields.Add(new FieldAnnotation { Name = "清洗液量", Offset = dataOffset + 3, Length = 2, RawHex = RawHex(dataOffset + 3, 2), DisplayValue = $"{washVol}" });
        }
        break;
    }
    case 0x000E: // 常规项目测试
    {
        if (dataLenActual >= 20)
        {
            fields.Add(new FieldAnnotation { Name = "样本盘号", Offset = dataOffset, Length = 1, RawHex = RawHex(dataOffset, 1), DisplayValue = $"{RawData[dataOffset]}" });
            fields.Add(new FieldAnnotation { Name = "样本位置", Offset = dataOffset + 1, Length = 1, RawHex = RawHex(dataOffset + 1, 1), DisplayValue = $"{RawData[dataOffset + 1]}" });
            fields.Add(new FieldAnnotation { Name = "稀释液位置", Offset = dataOffset + 2, Length = 1, RawHex = RawHex(dataOffset + 2, 1), DisplayValue = $"{RawData[dataOffset + 2]}" });
            fields.Add(new FieldAnnotation { Name = "样本杯类型", Offset = dataOffset + 3, Length = 1, RawHex = RawHex(dataOffset + 3, 1), DisplayValue = $"{RawData[dataOffset + 3]}" });
            fields.Add(new FieldAnnotation { Name = "交叉污染杯号", Offset = dataOffset + 4, Length = 1, RawHex = RawHex(dataOffset + 4, 1), DisplayValue = $"{RawData[dataOffset + 4]}" });
            fields.Add(new FieldAnnotation { Name = "清洗液位置", Offset = dataOffset + 5, Length = 1, RawHex = RawHex(dataOffset + 5, 1), DisplayValue = $"{RawData[dataOffset + 5]}" });
            fields.Add(new FieldAnnotation { Name = "试剂1编号", Offset = dataOffset + 6, Length = 1, RawHex = RawHex(dataOffset + 6, 1), DisplayValue = $"{RawData[dataOffset + 6]}" });
            fields.Add(new FieldAnnotation { Name = "试剂2编号", Offset = dataOffset + 7, Length = 1, RawHex = RawHex(dataOffset + 7, 1), DisplayValue = $"{RawData[dataOffset + 7]}" });
            fields.Add(new FieldAnnotation { Name = "试剂1量", Offset = dataOffset + 8, Length = 2, RawHex = RawHex(dataOffset + 8, 2), DisplayValue = $"{ToUInt16(dataOffset + 8)}" });
            fields.Add(new FieldAnnotation { Name = "试剂2量", Offset = dataOffset + 10, Length = 2, RawHex = RawHex(dataOffset + 10, 2), DisplayValue = $"{ToUInt16(dataOffset + 10)}" });
            var testNo = (uint)(ToUInt16(dataOffset + 12) | (ToUInt16(dataOffset + 14) << 16));
            fields.Add(new FieldAnnotation { Name = "样本测试号", Offset = dataOffset + 12, Length = 4, RawHex = RawHex(dataOffset + 12, 4), DisplayValue = $"{testNo}" });
            var sampleVol = ToUInt16(dataOffset + 16);
            fields.Add(new FieldAnnotation { Name = "样本量", Offset = dataOffset + 16, Length = 2, RawHex = RawHex(dataOffset + 16, 2), DisplayValue = $"{sampleVol / 10.0:F1}" });
            var diluteVol = ToUInt16(dataOffset + 18);
            fields.Add(new FieldAnnotation { Name = "样本稀释液量", Offset = dataOffset + 18, Length = 2, RawHex = RawHex(dataOffset + 18, 2), DisplayValue = $"{diluteVol}" });
        }
        break;
    }
    case 0x0012: // 样本条码扫描完成
    case 0x0014: // 试剂条码扫描完成
    {
        int slotSize = 19;
        int maxSlots = cmd == 0x0012 ? 40 : 80;
        int count = Math.Min(dataLenActual / slotSize, maxSlots);
        for (int i = 0; i < count; i++)
        {
            int off = dataOffset + i * slotSize;
            var barcode = System.Text.Encoding.ASCII.GetString(RawData, off, slotSize).TrimEnd('\0', ' ');
            if (!string.IsNullOrWhiteSpace(barcode))
            {
                var label = cmd == 0x0012 ? $"样本{i + 1}条码" : $"试剂{i + 1}条码";
                fields.Add(new FieldAnnotation { Name = label, Offset = off, Length = slotSize, RawHex = RawHex(off, slotSize), DisplayValue = barcode });
            }
        }
        break;
    }
    case 0x0016: // 试剂余量检测完成
    {
        int count = Math.Min(dataLenActual, 80);
        for (int i = 0; i < count; i++)
        {
            var pct = RawData[dataOffset + i];
            if (pct != 0xFF)
            {
                var color = pct < 10 ? "#EF5350" : pct < 30 ? "#FF9800" : "#22C55E";
                var severity = pct < 10 ? FieldSeverity.Error : pct < 30 ? FieldSeverity.Warning : FieldSeverity.Normal;
                fields.Add(new FieldAnnotation { Name = $"试剂{i + 1}余量", Offset = dataOffset + i, Length = 1, RawHex = RawHex(dataOffset + i, 1), DisplayValue = $"{pct}%", Color = color, Severity = severity });
            }
        }
        break;
    }
}

return fields;
