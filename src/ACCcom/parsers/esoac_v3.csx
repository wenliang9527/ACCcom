using System.Text.RegularExpressions;

var fields = new List<FieldAnnotation>();

if (RawData.Length == 0)
    return fields;

// 判断是二进制协议帧还是文本打印日志
bool isBinary = RawData.Length >= 2 && RawData[0] == 0xA5 && RawData[1] == 0x5A;

if (isBinary)
{
    ParseBinaryFrame(fields);
}
else
{
    var text = System.Text.Encoding.ASCII.GetString(RawData).TrimEnd('\r', '\n', '\0');
    ParseTextLog(fields, text);
}

return fields;

// ===================================================================
//  二进制协议帧解析
// ===================================================================

void ParseBinaryFrame(List<FieldAnnotation> f)
{
    if (RawData.Length < 9)
    {
        f.Add(new FieldAnnotation { Name = "错误", Offset = 0, Length = RawData.Length, RawHex = RawHex(0, RawData.Length), DisplayValue = "帧长度不足9字节", Color = "#EF5350", Severity = FieldSeverity.Error });
        return;
    }

    int offset = 0;

    var header = RawHex(offset, 2);
    bool headerOk = RawData[0] == 0xA5 && RawData[1] == 0x5A;
    f.Add(new FieldAnnotation
    {
        Name = "帧头", Offset = offset, Length = 2, RawHex = header,
        DisplayValue = headerOk ? "A5 5A (正确)" : $"A5 5A (错误: 实际={header})",
        Color = headerOk ? "#3478F6" : "#EF5350",
        Severity = headerOk ? FieldSeverity.Normal : FieldSeverity.Error
    });
    offset += 2;

    var totalLen = RawData[offset];
    f.Add(new FieldAnnotation { Name = "数据总长", Offset = offset, Length = 1, RawHex = RawHex(offset, 1), DisplayValue = $"{totalLen} 字节" });
    offset += 1;

    if (offset >= RawData.Length) return;

    var contentType = RawData[offset];
    bool contentTypeOk = contentType == 0x07;
    f.Add(new FieldAnnotation
    {
        Name = "内容类型", Offset = offset, Length = 1, RawHex = RawHex(offset, 1),
        DisplayValue = contentTypeOk ? "0x07 (结构化数据)" : $"0x{contentType:X2} (未知)",
        Color = contentTypeOk ? "#3478F6" : "#FF9800",
        Severity = contentTypeOk ? FieldSeverity.Normal : FieldSeverity.Warning
    });
    offset += 1;

    if (offset >= RawData.Length) return;

    var payloadLen = RawData[offset];
    f.Add(new FieldAnnotation { Name = "有效数据长", Offset = offset, Length = 1, RawHex = RawHex(offset, 1), DisplayValue = $"{payloadLen} 字节" });
    offset += 1;

    if (offset >= RawData.Length) return;

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
        0x03 => "#22C55E",
        0x05 or 0x07 or 0x08 => "#3478F6",
        0x85 or 0x87 or 0x88 => "#FF9800",
        _ => "#EF5350"
    };

    var cmdSeverity = cmdCode switch
    {
        0x03 or 0x05 or 0x07 or 0x08 or 0x85 or 0x87 or 0x88 => FieldSeverity.Normal,
        _ => FieldSeverity.Error
    };

    f.Add(new FieldAnnotation
    {
        Name = "指令码", Offset = offset, Length = 1, RawHex = RawHex(offset, 1),
        DisplayValue = $"0x{cmdCode:X2} ({cmdName})", Color = cmdColor, Severity = cmdSeverity
    });
    offset += 1;

    if (offset >= RawData.Length) return;

    var rn = ToUInt16(offset);
    f.Add(new FieldAnnotation { Name = "随机数(RN)", Offset = offset, Length = 2, RawHex = RawHex(offset, 2), DisplayValue = $"0x{rn:X4}" });
    offset += 2;

    switch (cmdCode)
    {
        case 0x03:
            ParseHeartbeat(f, offset, payloadLen);
            break;
        case 0x85:
            ParseSubCmd(f, offset, "query", "查询");
            break;
        case 0x87:
            ParseSubCmd(f, offset, "ctrl", "控制");
            break;
        case 0x88:
            ParseSubCmd(f, offset, "modify", "修改");
            break;
        case 0x05:
            ParseQueryResp(f, offset, payloadLen);
            break;
        case 0x07:
            ParseCtrlResp(f, offset, payloadLen);
            break;
        case 0x08:
            ParseModifyResp(f, offset, payloadLen);
            break;
        default:
            if (payloadLen > 2 && payloadLen - 2 > 0)
                f.Add(new FieldAnnotation { Name = "数据", Offset = offset, Length = payloadLen - 2, RawHex = RawHex(offset, payloadLen - 2), DisplayValue = $"{payloadLen - 2} 字节", Color = "#888888" });
            break;
    }

    int crcOffset = RawData.Length - 3;
    if (crcOffset >= offset)
    {
        var storedCrc = ToUInt16(crcOffset);
        var calcCrc = Crc16(0, crcOffset);
        bool crcOk = storedCrc == calcCrc;
        f.Add(new FieldAnnotation
        {
            Name = "CRC16", Offset = crcOffset, Length = 2, RawHex = RawHex(crcOffset, 2),
            DisplayValue = crcOk ? $"0x{storedCrc:X4} (正确)" : $"0x{storedCrc:X4} (计算值=0x{calcCrc:X4})",
            Color = crcOk ? "#22C55E" : "#EF5350",
            Severity = crcOk ? FieldSeverity.Normal : FieldSeverity.Error
        });
    }

    int footerOffset = RawData.Length - 1;
    if (footerOffset > offset)
    {
        bool footerOk = RawData[footerOffset] == 0xDD;
        f.Add(new FieldAnnotation
        {
            Name = "帧尾", Offset = footerOffset, Length = 1, RawHex = RawHex(footerOffset, 1),
            DisplayValue = footerOk ? "DD (正确)" : $"0x{RawData[footerOffset]:X2} (期望DD)",
            Color = footerOk ? "#3478F6" : "#EF5350",
            Severity = footerOk ? FieldSeverity.Normal : FieldSeverity.Error
        });
    }
}

// ===================================================================
//  文本打印日志解析
// ===================================================================

void ParseTextLog(List<FieldAnnotation> f, string text)
{
    if (string.IsNullOrEmpty(text))
    {
        f.Add(new FieldAnnotation { Name = "空行", Offset = 0, Length = RawData.Length, RawHex = RawHex(0, RawData.Length), DisplayValue = "(空)", Color = "#888888" });
        return;
    }

    if (TryParseHeartbeatText(f, text)) return;
    if (TryParseBleFrame(f, text)) return;
    if (TryParseProtocolEntry(f, text)) return;
    if (TryParseProtocolError(f, text)) return;
    if (TryParseUnknownCmd(f, text)) return;
    if (TryParseMqttState(f, text)) return;
    if (TryParseMl307(f, text)) return;
    if (TryParseBleEvent(f, text)) return;
    if (TryParseIrLog(f, text)) return;
    if (TryParseDeviceConfig(f, text)) return;
    if (TryParseMqttConfig(f, text)) return;
    if (TryParseAircondata(f, text)) return;
    if (TryParseSpiFlash(f, text)) return;
    if (TryParseIotReassembly(f, text)) return;
    if (TryParseUrc(f, text)) return;
    if (TryParseSensor(f, text)) return;
    if (TryParseKeyEvent(f, text)) return;
    if (TryParseGattEvent(f, text)) return;
    if (TryParseEsairCcc(f, text)) return;
    if (TryParseOta(f, text)) return;
    if (TryParseIotToMqtt(f, text)) return;

    f.Add(new FieldAnnotation { Name = "文本日志", Offset = 0, Length = RawData.Length, RawHex = RawHex(0, RawData.Length), DisplayValue = text, Color = "#888888" });
}

// ---- [Heartbeat] ----

bool TryParseHeartbeatText(List<FieldAnnotation> f, string text)
{
    var m = Regex.Match(text, @"^\[Heartbeat\]\s+Power:(\d+)\.?(\d*)W\s+Temp:(\d+)\.?(\d*)C\s+Status:(ON|OFF)\s+Mode:(\w+)\s+SetTemp:(\d+)\s+Wind:(\d+)");
    if (!m.Success) return false;

    f.Add(new FieldAnnotation { Name = "日志类别", Offset = 0, Length = 11, RawHex = RawHex(0, 11), DisplayValue = "[Heartbeat] 心跳上报", Color = "#22C55E" });
    f.Add(new FieldAnnotation { Name = "功率", Offset = 0, Length = RawData.Length, RawHex = "", DisplayValue = $"{m.Groups[1].Value}.{m.Groups[2].Value}W", Color = "#FF9800" });
    f.Add(new FieldAnnotation { Name = "温度", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"{m.Groups[3].Value}.{m.Groups[4].Value}°C", Color = "#FF9800" });
    f.Add(new FieldAnnotation { Name = "开关", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[5].Value, Color = m.Groups[5].Value == "ON" ? "#22C55E" : "#888888" });
    f.Add(new FieldAnnotation { Name = "模式", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[6].Value });
    f.Add(new FieldAnnotation { Name = "设定温度", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"{m.Groups[7].Value}°C" });
    f.Add(new FieldAnnotation { Name = "风速", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[8].Value });
    return true;
}

// ---- [BLE] ----

bool TryParseBleFrame(List<FieldAnnotation> f, string text)
{
    var m = Regex.Match(text, @"^\[BLE\]\s+(\S+)\s+(OK|CRC_ERR)");
    if (!m.Success) return false;

    var cmdName = m.Groups[1].Value;
    var crcStatus = m.Groups[2].Value;
    bool crcOk = crcStatus == "OK";

    // 映射命令名称到协议
    string protoDesc = GetBleCmdDescription(cmdName);

    f.Add(new FieldAnnotation { Name = "日志类别", Offset = 0, Length = 5, RawHex = RawHex(0, 5), DisplayValue = "[BLE] 帧跟踪", Color = "#3478F6" });
    f.Add(new FieldAnnotation { Name = "命令", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"{cmdName} ({protoDesc})", Color = "#3478F6" });
    f.Add(new FieldAnnotation { Name = "CRC状态", Offset = 0, Length = 0, RawHex = "", DisplayValue = crcOk ? "校验通过" : "校验失败", Color = crcOk ? "#22C55E" : "#EF5350", Severity = crcOk ? FieldSeverity.Normal : FieldSeverity.Error });
    return true;
}

string GetBleCmdDescription(string name)
{
    return name switch
    {
        "REPORT" => "0x03 心跳上报",
        "QUERY_RESP" => "0x05 查询回复",
        "CTRL_RESP" => "0x07 控制回复",
        "MODIFY_RESP" => "0x08 修改回复",
        "Q_DEVICE_INFO" => "0x85/0x01 查询设备信息",
        "Q_STATUS" => "0x85/0x02 查询设备状态",
        "Q_POWER" => "0x85/0x03 查询功率",
        "Q_TEMP" => "0x85/0x04 查询温度",
        "Q_ADC" => "0x85/0x05 查询ADC",
        "Q_BLE_NAME" => "0x85/0x06 查询BLE名称",
        "Q_DEV_ADDR" => "0x85/0x07 查询设备地址",
        "Q_TIMER" => "0x85/0x08 查询定时状态",
        "Q_MQTT_CFG" => "0x85/0x09 查询MQTT配置",
        "Q_LISTEN" => "0x85/0x0A 查询监听状态",
        "Q_IR_DATA" => "0x85/0x0B 查询红外数据",
        "Q_IOT_DEVID" => "0x85/0x0C 查询设备ID",
        "Q_IOT_PRDID" => "0x85/0x0D 查询产品ID",
        "Q_IOT_KEY" => "0x85/0x0E 查询设备KEY",
        "Q_PLATFORM" => "0x85/0x0F 查询平台类型",
        "Q_DOMAIN" => "0x85/0x10 查询域名端口",
        "C_POWER" => "0x87/0x01 开关控制",
        "C_MODE" => "0x87/0x02 模式控制",
        "C_TEMP" => "0x87/0x03 温度设置",
        "C_WIND" => "0x87/0x04 风速设置",
        "C_TEMP_UP" => "0x87/0x05 温度增加",
        "C_TEMP_DN" => "0x87/0x06 温度减少",
        "C_IR_LEARN" => "0x87/0x07 红外学习开始",
        "C_IR_STOP" => "0x87/0x08 红外学习停止",
        "C_IR_SEND" => "0x87/0x09 红外发送",
        "C_IR_SAVE" => "0x87/0x0A 保存红外按键",
        "C_LISTEN_SW" => "0x87/0x0B 监听总开关",
        "C_LISTEN_MD" => "0x87/0x0C 切换监听模式",
        "C_SYNC_TIME" => "0x87/0x0D 时间同步",
        "C_BLE_NAME" => "0x87/0x0E 设置BLE名称",
        "C_DEV_ADDR" => "0x87/0x0F 设置设备地址",
        "C_TIMER" => "0x87/0x10 设置定时关",
        "C_TIMER_PRM" => "0x87/0x11 设置定时参数",
        "C_MQTT_CFG" => "0x87/0x12 设置MQTT配置",
        "M_IOT_DEVID" => "0x88/0x13 修改设备ID",
        "M_IOT_PRDID" => "0x88/0x14 修改产品ID",
        "M_IOT_KEY" => "0x88/0x15 修改设备KEY",
        "M_PLATFORM" => "0x88/0x18 修改平台类型",
        "M_DOMAIN" => "0x88/0x19 修改域名端口",
        _ => $"未知命令"
    };
}

// ---- Protocol: ... ----

bool TryParseProtocolEntry(List<FieldAnnotation> f, string text)
{
    var m = Regex.Match(text, @"^Protocol:\s+cmd=0x([0-9A-Fa-f]{2})\s+sub=0x([0-9A-Fa-f]{2})\s+params_len=(\d+)\s+src=(\d+)");
    if (!m.Success) return false;

    var cmd = (byte)Convert.ToUInt16(m.Groups[1].Value, 16);
    var sub = (byte)Convert.ToUInt16(m.Groups[2].Value, 16);
    var paramsLen = int.Parse(m.Groups[3].Value);
    var src = int.Parse(m.Groups[4].Value);

    var cmdTypeName = cmd switch { 0x85 => "查询", 0x87 => "控制", 0x88 => "修改", _ => $"0x{cmd:X2}" };
    var subName = GetSubCmdName(
        cmd switch { 0x85 => "query", 0x87 => "ctrl", 0x88 => "modify", _ => "" }, sub);
    var srcName = src switch { 0 => "BLE", 1 => "UART", 2 => "MQTT", _ => $"{src}" };

    f.Add(new FieldAnnotation { Name = "日志类别", Offset = 0, Length = 9, RawHex = RawHex(0, 9), DisplayValue = "[Protocol] 协议入口", Color = "#3478F6" });
    f.Add(new FieldAnnotation { Name = "指令", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"{cmdTypeName}指令 0x{cmd:X2}/0x{sub:X2} {subName}" });
    f.Add(new FieldAnnotation { Name = "参数长度", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"{paramsLen} 字节" });
    f.Add(new FieldAnnotation { Name = "来源", Offset = 0, Length = 0, RawHex = "", DisplayValue = srcName });
    return true;
}

bool TryParseProtocolError(List<FieldAnnotation> f, string text)
{
    // Protocol: payload too short (%d)
    var m = Regex.Match(text, @"^Protocol:\s+payload too short \((\d+)\)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别", Offset = 0, Length = 0, RawHex = "", DisplayValue = "[Protocol] 帧校验错误", Color = "#EF5350", Severity = FieldSeverity.Error });
        f.Add(new FieldAnnotation { Name = "错误", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"有效数据长度不足({m.Groups[1].Value} < 4)", Color = "#EF5350", Severity = FieldSeverity.Error });
        return true;
    }

    // Protocol: Unknown cmd_code=0x%02X
    m = Regex.Match(text, @"^Protocol:\s+Unknown cmd_code=0x([0-9A-Fa-f]{2})");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别", Offset = 0, Length = 0, RawHex = "", DisplayValue = "[Protocol] 未知指令码", Color = "#EF5350", Severity = FieldSeverity.Error });
        f.Add(new FieldAnnotation { Name = "指令码", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"0x{m.Groups[1].Value}", Color = "#EF5350", Severity = FieldSeverity.Error });
        return true;
    }

    return false;
}

bool TryParseUnknownCmd(List<FieldAnnotation> f, string text)
{
    var m = Regex.Match(text, @"^(CTRL|QUERY|MODIFY):\s+Unknown sub_cmd=0x([0-9A-Fa-f]{2})");
    if (!m.Success) return false;

    var typeName = m.Groups[1].Value switch { "CTRL" => "控制", "QUERY" => "查询", "MODIFY" => "修改", _ => m.Groups[1].Value };
    var subHex = m.Groups[2].Value;

    f.Add(new FieldAnnotation { Name = "日志类别", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"[{m.Groups[1].Value}] 未知子命令", Color = "#FF9800", Severity = FieldSeverity.Warning });
    f.Add(new FieldAnnotation { Name = "错误", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"{typeName}子命令 0x{subHex} 无法识别", Color = "#FF9800", Severity = FieldSeverity.Warning });
    return true;
}

// ---- MQTT ---- 

bool TryParseMqttState(List<FieldAnnotation> f, string text)
{
    string? desc = null;
    string cat = "MQTT";
    string color = "#8B5CF6";
    var sev = FieldSeverity.Normal;

    if (text == "APP: MQTT connected") { desc = "MQTT 已连接"; color = "#22C55E"; }
    else if (text == "APP: MQTT disconnected") { desc = "MQTT 已断开"; color = "#EF5350"; sev = FieldSeverity.Warning; }
    else if (text == "APP: MQTT data processed") { desc = "MQTT 下行数据处理完成"; }
    else if (text == "MQTT config updated & saved") { desc = "MQTT 配置已更新并保存"; color = "#22C55E"; }
    else if (text == "MQTT config save FAILED") { desc = "MQTT 配置保存失败"; color = "#EF5350"; sev = FieldSeverity.Error; }
    else if (text == "MQTT reconnect timer started (200ms drive)") { desc = "MQTT 重连定时器启动(200ms周期)"; cat = "MQTT重连"; }
    else if (text == "MQTT reconnect timer started (pending DISC)") { desc = "MQTT 重连定时器启动(等待断开)"; cat = "MQTT重连"; }
    else if (text == "MQTT reconnect timer stopped") { desc = "MQTT 重连定时器停止"; cat = "MQTT重连"; cat = "MQTT重连"; }
    else if (text == "APP: MQTT config updated, reconnecting...") { desc = "MQTT 配置更新，触发重连"; }
    else if (text == "APP: MQTTDISC deferred (UART busy)") { desc = "MQTT 断开命令延迟(UART忙)"; color = "#FF9800"; sev = FieldSeverity.Warning; }
    else if (text == "APP: IOT config apply (debounce timer)") { desc = "IOT→MQTT 配置转换防抖定时器触发"; cat = "IOT"; }

    if (desc != null)
    {
        f.Add(new FieldAnnotation { Name = $"日志类别({cat})", Offset = 0, Length = RawData.Length, RawHex = "", DisplayValue = desc, Color = color, Severity = sev });
        return true;
    }

    // MQTT reconnect success (steps: %d)
    var m = Regex.Match(text, @"^MQTT reconnect success \(steps:\s*(\d+)\)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(MQTT重连)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "MQTT 重连成功", Color = "#22C55E" });
        f.Add(new FieldAnnotation { Name = "步骤数", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[1].Value });
        return true;
    }

    m = Regex.Match(text, @"^MQTT:\s+total steps limit \((\d+)\) reached");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(MQTT重连)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "MQTT 状态机步数超限", Color = "#FF9800", Severity = FieldSeverity.Warning });
        f.Add(new FieldAnnotation { Name = "限制", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"{m.Groups[1].Value}步" });
        return true;
    }

    m = Regex.Match(text, @"^MQTT:\s+max retry \((\d+)\), pausing 120s");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(MQTT重连)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "MQTT 重试达上限，暂停120秒", Color = "#EF5350", Severity = FieldSeverity.Error });
        f.Add(new FieldAnnotation { Name = "重试次数", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[1].Value });
        return true;
    }

    m = Regex.Match(text, @"^MQTT:\s+retry (\d+)/(\d+), pausing 30s");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(MQTT重连)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "MQTT 重连失败，暂停30秒", Color = "#FF9800", Severity = FieldSeverity.Warning });
        f.Add(new FieldAnnotation { Name = "重试", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"{m.Groups[1].Value}/{m.Groups[2].Value}" });
        return true;
    }

    return false;
}

// ---- ML307A 4G ----

bool TryParseMl307(List<FieldAnnotation> f, string text)
{
    string? desc = null;
    string color = "#EC4899";
    var sev = FieldSeverity.Normal;

    if (text == "ML307_TURN_ON") { desc = "ML307A 模组开机信号已发送"; color = "#EC4899"; }
    else if (text == "ML307_Module_ERROR") { desc = "ML307A 模组错误状态"; color = "#EF5350"; sev = FieldSeverity.Error; }
    else if (text == "ML307A: Module ready (+MATREADY received)") { desc = "ML307A 模组就绪"; color = "#22C55E"; }
    else if (text == "ML307A: Module not ready (timeout waiting for +MATREADY)") { desc = "ML307A 模组就绪超时"; color = "#EF5350"; sev = FieldSeverity.Error; }
    else if (text == "ML307A: Module ready (+MATREADY in AT accum)") { desc = "ML307A 模组就绪(AT缓冲检测)"; color = "#22C55E"; }
    else if (text == "AT rx trunc") { desc = "AT 接收缓冲区溢出，数据截断"; color = "#FF9800"; sev = FieldSeverity.Warning; }

    if (desc != null)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(ML307A)", Offset = 0, Length = RawData.Length, RawHex = "", DisplayValue = desc, Color = color, Severity = sev });
        return true;
    }

    // Error: Command too long or invalid.
    if (text == "Error: Command too long or invalid.")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(AT)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "AT 命令超长或无效", Color = "#EF5350", Severity = FieldSeverity.Error });
        return true;
    }

    // MQTT CONN failed, conn_state=%d
    var m = Regex.Match(text, @"^MQTT CONN failed, conn_state=(\d+)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(ML307A)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "MQTT AT连接失败", Color = "#EF5350", Severity = FieldSeverity.Error });
        f.Add(new FieldAnnotation { Name = "连接状态码", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[1].Value });
        return true;
    }

    // AT_RESP: prefix=%s, parsed stat=%d, expect=%d
    m = Regex.Match(text, @"^AT_RESP:\s+prefix=(.+),\s+parsed stat=(\d+),\s+expect=(\d+)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(AT)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "AT 响应解析" });
        f.Add(new FieldAnnotation { Name = "前缀", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[1].Value });
        f.Add(new FieldAnnotation { Name = "解析值", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[2].Value });
        f.Add(new FieldAnnotation { Name = "期望值", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[3].Value });
        return true;
    }

    // SM: scan[%d]: ...
    m = Regex.Match(text, @"^SM:\s+scan\[(\d+)\]:");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(AT)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "AT 缓冲扫描", Color = "#888888" });
        f.Add(new FieldAnnotation { Name = "位置", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"偏移 {m.Groups[1].Value}" });
        return true;
    }

    return false;
}

// ---- BLE 协议栈事件 ----

bool TryParseBleEvent(List<FieldAnnotation> f, string text)
{
    // slave[%d],connect. link_num:%d
    var m = Regex.Match(text, @"^slave\[(\d+)\],connect\. link_num:(\d+)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(BLE)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "BLE 从机连接", Color = "#22C55E" });
        f.Add(new FieldAnnotation { Name = "连接索引", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[1].Value });
        f.Add(new FieldAnnotation { Name = "链路号", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[2].Value });
        return true;
    }

    // Start advertising, name=%s, rsp_len=%d
    m = Regex.Match(text, @"^Start advertising, name=(.+), rsp_len=(\d+)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(BLE)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "BLE 开始广播" });
        f.Add(new FieldAnnotation { Name = "蓝牙名称", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[1].Value });
        f.Add(new FieldAnnotation { Name = "扫描响应长度", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[2].Value });
        return true;
    }

    // Link[%d] disconnect,reason:0x%02X
    m = Regex.Match(text, @"^Link\[(\d+)\]\s+disconnect,reason:0x([0-9A-Fa-f]{2})");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(BLE)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "BLE 连接断开", Color = "#FF9800", Severity = FieldSeverity.Warning });
        f.Add(new FieldAnnotation { Name = "链路", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"{m.Groups[1].Value}" });
        f.Add(new FieldAnnotation { Name = "断开原因码", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"0x{m.Groups[2].Value}" });
        return true;
    }

    // Link[%d]param update,interval:%d,latency:%d,timeout:%d
    m = Regex.Match(text, @"^Link\[(\d+)\]param update,interval:(\d+),latency:(\d+),timeout:(\d+)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(BLE)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "BLE 连接参数更新" });
        f.Add(new FieldAnnotation { Name = "链路", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[1].Value });
        f.Add(new FieldAnnotation { Name = "间隔", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[2].Value });
        f.Add(new FieldAnnotation { Name = "延迟", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[3].Value });
        f.Add(new FieldAnnotation { Name = "超时", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[4].Value });
        return true;
    }

    // Link[%d]param reject,status:0x%02x
    m = Regex.Match(text, @"^Link\[(\d+)\]param reject,status:0x([0-9A-Fa-f]{2})");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(BLE)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "BLE 参数更新被拒绝", Color = "#FF9800", Severity = FieldSeverity.Warning });
        f.Add(new FieldAnnotation { Name = "链路", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[1].Value });
        f.Add(new FieldAnnotation { Name = "状态码", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"0x{m.Groups[2].Value}" });
        return true;
    }

    // mtu update,conidx=%d,mtu=%d
    m = Regex.Match(text, @"^mtu update,conidx=(\d+),mtu=(\d+)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(BLE)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "BLE MTU 更新" });
        f.Add(new FieldAnnotation { Name = "连接索引", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[1].Value });
        f.Add(new FieldAnnotation { Name = "MTU", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[2].Value });
        return true;
    }

    // link rssi %d
    m = Regex.Match(text, @"^link rssi (-?\d+)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(BLE)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "BLE 信号强度" });
        f.Add(new FieldAnnotation { Name = "RSSI", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"{m.Groups[1].Value} dBm" });
        return true;
    }

    // slave[%d]_encrypted
    m = Regex.Match(text, @"^slave\[(\d+)\]_encrypted");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(BLE)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "BLE 从机加密完成", Color = "#22C55E" });
        f.Add(new FieldAnnotation { Name = "连接索引", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[1].Value });
        return true;
    }

    // BLE name loaded from flash: "%s"
    m = Regex.Match(text, @"^BLE name loaded from flash:\s+""(.+)""");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(BLE名称)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "BLE 名称从 Flash 加载", Color = "#3478F6" });
        f.Add(new FieldAnnotation { Name = "名称", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[1].Value });
        return true;
    }

    // BLE name generated from BD_ADDR: "%s"
    m = Regex.Match(text, @"^BLE name generated from BD_ADDR:\s+""(.+)""");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(BLE名称)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "BLE 名称由地址自动生成", Color = "#3478F6" });
        f.Add(new FieldAnnotation { Name = "名称", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[1].Value });
        return true;
    }

    // BLE name updated to "%s" ...
    m = Regex.Match(text, @"^BLE name updated to\s+""(.+)""\s+(.*)");
    if (m.Success)
    {
        var suffix = m.Groups[2].Value;
        f.Add(new FieldAnnotation { Name = "日志类别(BLE名称)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "BLE 名称已更新", Color = "#22C55E" });
        f.Add(new FieldAnnotation { Name = "新名称", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[1].Value });
        f.Add(new FieldAnnotation { Name = "备注", Offset = 0, Length = 0, RawHex = "", DisplayValue = suffix.Contains("W25Q") ? "已保存至 Flash" : "重启后丢失", Color = suffix.Contains("saved") ? "#22C55E" : "#FF9800" });
        return true;
    }

    // ble_update_device_name: invalid param (len=%d)
    m = Regex.Match(text, @"^ble_update_device_name: invalid param \(len=(\d+)\)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(BLE名称)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "更新BLE名称参数无效", Color = "#EF5350", Severity = FieldSeverity.Error });
        f.Add(new FieldAnnotation { Name = "长度", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[1].Value });
        return true;
    }

    // ble_update_device_name: name invalid, status=%d
    m = Regex.Match(text, @"^ble_update_device_name: name invalid, status=(\d+)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(BLE名称)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "BLE 名称违反协议", Color = "#EF5350", Severity = FieldSeverity.Error });
        f.Add(new FieldAnnotation { Name = "状态", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[1].Value });
        return true;
    }

    // All service added
    if (text == "All service added")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(BLE)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "BLE 所有服务注册完成", Color = "#22C55E" });
        return true;
    }

    // adv_end,status:0x%02x
    m = Regex.Match(text, @"^adv_end,status:0x([0-9A-Fa-f]{2})");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(BLE)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "BLE 广播结束", Color = "#888888" });
        f.Add(new FieldAnnotation { Name = "状态码", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"0x{m.Groups[1].Value}" });
        return true;
    }

    // adv abnormal, retry after 1s
    if (text == "adv abnormal, retry after 1s")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(BLE)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "BLE 广播异常，1秒后重试", Color = "#FF9800", Severity = FieldSeverity.Warning });
        return true;
    }

    // Local BDADDR: ...
    if (text.StartsWith("Local BDADDR:"))
    {
        f.Add(new FieldAnnotation { Name = "日志类别(BLE)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "BLE 本机蓝牙地址" });
        return true;
    }

    return false;
}

// ---- 红外 ----

bool TryParseIrLog(List<FieldAnnotation> f, string text)
{
    if (text == "IR_start_learn!") { f.Add(new FieldAnnotation { Name = "日志类别(红外)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "红外学习开始", Color = "#FF9800" }); return true; }
    if (text == "IR_stop_learn!") { f.Add(new FieldAnnotation { Name = "日志类别(红外)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "红外学习停止", Color = "#FF9800" }); return true; }
    if (text == "IR learn failed!") { f.Add(new FieldAnnotation { Name = "日志类别(红外)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "红外学习失败", Color = "#EF5350", Severity = FieldSeverity.Error }); return true; }
    if (text == "IR Send End") { f.Add(new FieldAnnotation { Name = "日志类别(红外)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "红外发送完成", Color = "#22C55E" }); return true; }
    if (text == "IR start_learn Fail!") { f.Add(new FieldAnnotation { Name = "日志类别(红外)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "红外学习启动失败", Color = "#EF5350", Severity = FieldSeverity.Error }); return true; }
    if (text == "IR send busy!") { f.Add(new FieldAnnotation { Name = "日志类别(红外)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "红外发送忙", Color = "#FF9800", Severity = FieldSeverity.Warning }); return true; }
    if (text == "ir_learn busy!") { f.Add(new FieldAnnotation { Name = "日志类别(红外)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "红外学习忙", Color = "#FF9800", Severity = FieldSeverity.Warning }); return true; }
    if (text == "ir_learn malloc error!") { f.Add(new FieldAnnotation { Name = "日志类别(红外)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "红外学习内存分配失败", Color = "#EF5350", Severity = FieldSeverity.Error }); return true; }
    if (text == "Please perform IR learn first") { f.Add(new FieldAnnotation { Name = "日志类别(红外)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "请先进行红外学习", Color = "#FF9800", Severity = FieldSeverity.Warning }); return true; }
    if (text == "data_size oversize!") { f.Add(new FieldAnnotation { Name = "日志类别(红外)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "红外数据超限", Color = "#EF5350", Severity = FieldSeverity.Error }); return true; }

    // IR learn success! ir_learn_data.ir_carrier_fre = %d
    var m = Regex.Match(text, @"^IR learn success! ir_learn_data\.ir_carrier_fre = (\d+)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(红外)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "红外学习成功", Color = "#22C55E" });
        f.Add(new FieldAnnotation { Name = "载波频率", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"{m.Groups[1].Value} Hz" });
        return true;
    }

    // ir_learn->ir_learn_step = IR_LEARN_GET_DATA
    if (text.Contains("ir_learn_step = IR_LEARN_GET_DATA"))
    {
        f.Add(new FieldAnnotation { Name = "日志类别(红外)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "红外进入数据采集阶段", Color = "#3478F6" });
        return true;
    }

    // send IR learn data, ir_learn_data.ir_learn_data_cnt=%d
    m = Regex.Match(text, @"^send IR learn data, ir_learn_data\.ir_learn_data_cnt=(\d+)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(红外)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "发送已学习的红外数据", Color = "#3478F6" });
        f.Add(new FieldAnnotation { Name = "数据点数", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[1].Value });
        return true;
    }

    return false;
}

// ---- 设备配置 Flash ----

bool TryParseDeviceConfig(List<FieldAnnotation> f, string text)
{
    // device_config: W25Q absent, no stored BLE name
    if (text == "device_config: W25Q absent, no stored BLE name")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(设备配置)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "无 W25Q Flash，无存储的 BLE 名", Color = "#FF9800", Severity = FieldSeverity.Warning });
        return true;
    }

    // device_config: no valid config in flash, init empty custom name
    if (text == "device_config: no valid config in flash, init empty custom name")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(设备配置)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "Flash 配置无效，使用空名称", Color = "#FF9800", Severity = FieldSeverity.Warning });
        return true;
    }

    // device_config: loaded, ble_name="%s", len=%d
    var m = Regex.Match(text, @"^device_config: loaded, ble_name=""(.+)"", len=(\d+)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(设备配置)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "BLE 名称从 Flash 加载成功", Color = "#22C55E" });
        f.Add(new FieldAnnotation { Name = "BLE名称", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[1].Value });
        f.Add(new FieldAnnotation { Name = "长度", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[2].Value });
        return true;
    }

    // device_config: loaded, no custom name (use hardware default)
    if (text == "device_config: loaded, no custom name (use hardware default)")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(设备配置)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "无自定义 BLE 名，使用硬件默认", Color = "#888888" });
        return true;
    }

    // ERR: device_config write skipped (no W25Q)
    if (text == "ERR: device_config write skipped (no W25Q)")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(设备配置)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "写入跳过(无 W25Q)", Color = "#FF9800", Severity = FieldSeverity.Warning });
        return true;
    }

    // ERR: device_config verify failed
    if (text.Contains("device_config verify failed"))
    {
        f.Add(new FieldAnnotation { Name = "日志类别(设备配置)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "Flash 写入校验失败", Color = "#EF5350", Severity = FieldSeverity.Error });
        return true;
    }

    return false;
}

// ---- MQTT 配置 ----

bool TryParseMqttConfig(List<FieldAnnotation> f, string text)
{
    // mqtt_config: loaded from flash
    if (text == "mqtt_config: loaded from flash")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(MQTT配置)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "MQTT 配置从 Flash 加载成功", Color = "#22C55E" });
        return true;
    }

    // mqtt_config: no valid config in flash
    if (text == "mqtt_config: no valid config in flash")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(MQTT配置)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "Flash 中无有效 MQTT 配置", Color = "#FF9800", Severity = FieldSeverity.Warning });
        return true;
    }

    // mqtt_config: W25Q absent, skip load
    if (text == "mqtt_config: W25Q absent, skip load")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(MQTT配置)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "无 W25Q，跳过 MQTT 配置加载", Color = "#FF9800", Severity = FieldSeverity.Warning });
        return true;
    }

    // mqtt_config: saved to flash
    if (text == "mqtt_config: saved to flash")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(MQTT配置)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "MQTT 配置已保存到 Flash", Color = "#22C55E" });
        return true;
    }

    // mqtt_config: version mismatch (stored=%d, current=%d)
    var m = Regex.Match(text, @"^mqtt_config: version mismatch \(stored=(\d+), current=(\d+)\)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(MQTT配置)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "MQTT 配置版本不匹配，使用默认值", Color = "#FF9800", Severity = FieldSeverity.Warning });
        f.Add(new FieldAnnotation { Name = "存储版本", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[1].Value });
        f.Add(new FieldAnnotation { Name = "当前版本", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[2].Value });
        return true;
    }

    // iot_config: loaded from flash
    if (text == "iot_config: loaded from flash")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(IoT配置)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "IoT 配置从 Flash 加载成功", Color = "#22C55E" });
        return true;
    }

    // iot_config: no valid config in flash
    if (text == "iot_config: no valid config in flash")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(IoT配置)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "Flash 中无有效 IoT 配置", Color = "#FF9800", Severity = FieldSeverity.Warning });
        return true;
    }

    // iot_config: saved to flash
    if (text == "iot_config: saved to flash")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(IoT配置)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "IoT 配置已保存到 Flash", Color = "#22C55E" });
        return true;
    }

    return false;
}

// ---- 空调数据持久化 ----

bool TryParseAircondata(List<FieldAnnotation> f, string text)
{
    // ESAirdata_Save: Saved (temp=%d, mode=%d, wind=%d, status=%s)
    var m = Regex.Match(text, @"^ESAirdata_Save: Saved \(temp=(\d+), mode=(\d+), wind=(\d+), status=(ON|OFF)\)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(空调数据)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "空调数据保存到 Flash", Color = "#22C55E" });
        f.Add(new FieldAnnotation { Name = "温度", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"{m.Groups[1].Value}°C" });
        f.Add(new FieldAnnotation { Name = "模式", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[2].Value });
        f.Add(new FieldAnnotation { Name = "风速", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[3].Value });
        f.Add(new FieldAnnotation { Name = "开关", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[4].Value });
        return true;
    }

    // ESAirdata_Load: Loaded (temp=%d, mode=%d, wind=%d, status=%s)
    m = Regex.Match(text, @"^ESAirdata_Load: Loaded \(temp=(\d+), mode=(\d+), wind=(\d+), status=(ON|OFF)\)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(空调数据)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "空调数据从 Flash 加载成功", Color = "#22C55E" });
        f.Add(new FieldAnnotation { Name = "温度", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"{m.Groups[1].Value}°C" });
        f.Add(new FieldAnnotation { Name = "模式", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[2].Value });
        f.Add(new FieldAnnotation { Name = "风速", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[3].Value });
        f.Add(new FieldAnnotation { Name = "开关", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[4].Value });
        return true;
    }

    // ESAirdata_ValidateRange: Temperature out of range: %d
    m = Regex.Match(text, @"^ESAirdata_ValidateRange: (Temperature|Wind speed|Mode|Status) out of range: (-?\d+)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(空调数据)", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"空调数据范围校验失败: {m.Groups[1].Value}", Color = "#EF5350", Severity = FieldSeverity.Error });
        f.Add(new FieldAnnotation { Name = "无效值", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[2].Value });
        return true;
    }

    // ESAirdata_SetDefault: Setting default values
    if (text == "ESAirdata_SetDefault: Setting default values")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(空调数据)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "空调数据重置为默认值", Color = "#FF9800", Severity = FieldSeverity.Warning });
        return true;
    }

    // ESAirdata_Load: Magic invalid / Version mismatch / Checksum error
    m = Regex.Match(text, @"^ESAirdata_Load: (Magic invalid \(0x[0-9A-Fa-f]{8}\)|Version mismatch|Checksum error)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(空调数据)", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"空调数据 Flash 校验失败: {m.Groups[1].Value}", Color = "#FF9800", Severity = FieldSeverity.Warning });
        return true;
    }

    // ESAirdata_TriggerSave: Save in 1s
    if (text == "ESAirdata_TriggerSave: Save in 1s")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(空调数据)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "空调数据触发延时保存(1秒后)", Color = "#3478F6" });
        return true;
    }

    return false;
}

// ---- SPI Flash ----

bool TryParseSpiFlash(List<FieldAnnotation> f, string text)
{
    // SPI Flash: W25Q detected (ID=0x%04X)
    var m = Regex.Match(text, @"^SPI Flash: W25Q detected \(ID=0x([0-9A-Fa-f]{4})\)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(SPI Flash)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "W25Q Flash 检测成功", Color = "#22C55E" });
        f.Add(new FieldAnnotation { Name = "设备ID", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"0x{m.Groups[1].Value}" });
        return true;
    }

    // SPI Flash: Not detected (ID=0x%04X)
    m = Regex.Match(text, @"^SPI Flash: Not detected \(ID=0x([0-9A-Fa-f]{4})\)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(SPI Flash)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "W25Q Flash 检测失败", Color = "#EF5350", Severity = FieldSeverity.Error });
        f.Add(new FieldAnnotation { Name = "读取ID", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"0x{m.Groups[1].Value}" });
        return true;
    }

    // [WARN] SpiFlash_* Flash not detected
    m = Regex.Match(text, @"^\[WARN\] SpiFlash_(\w+): Flash not detected");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(SPI Flash)", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"Flash 不存在，操作被跳过 ({m.Groups[1].Value})", Color = "#FF9800", Severity = FieldSeverity.Warning });
        return true;
    }

    return false;
}

// ---- IOT 分包重组 ----

bool TryParseIotReassembly(List<FieldAnnotation> f, string text)
{
    // IOT reassembly timeout, sub_cmd=0x%02X
    var m = Regex.Match(text, @"^IOT reassembly timeout, sub_cmd=0x([0-9A-Fa-f]{2})");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(IOT分包)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "IOT 分包重组超时", Color = "#EF5350", Severity = FieldSeverity.Error });
        f.Add(new FieldAnnotation { Name = "子命令", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"0x{m.Groups[1].Value}" });
        return true;
    }

    // IOT reassembly: duplicate pkg_no=%d
    m = Regex.Match(text, @"^IOT reassembly: duplicate pkg_no=(\d+)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(IOT分包)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "IOT 分包重复包号，丢弃", Color = "#FF9800", Severity = FieldSeverity.Warning });
        f.Add(new FieldAnnotation { Name = "包号", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[1].Value });
        return true;
    }

    return false;
}

// ---- URC 队列 ----

bool TryParseUrc(List<FieldAnnotation> f, string text)
{
    // URC queue full, dropped type=%d
    var m = Regex.Match(text, @"^URC queue full, dropped type=(\d+)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(URC)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "URC 队列满，丢弃消息", Color = "#EF5350", Severity = FieldSeverity.Error });
        f.Add(new FieldAnnotation { Name = "类型", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[1].Value });
        return true;
    }

    if (text == "URC queue full, dropped reasm publish")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(URC)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "URC 队列满，丢弃重组后的 MQTT Publish", Color = "#EF5350", Severity = FieldSeverity.Error });
        return true;
    }

    return false;
}

// ---- 传感器 ----

bool TryParseSensor(List<FieldAnnotation> f, string text)
{
    // temperature = %d,humidity = %d
    var m = Regex.Match(text, @"^temperature = (-?\d+),humidity = (-?\d+)");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(传感器)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "SHT3X 温湿度读数" });
        f.Add(new FieldAnnotation { Name = "温度", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"{m.Groups[1].Value}°C" });
        f.Add(new FieldAnnotation { Name = "湿度", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"{m.Groups[2].Value}%" });
        return true;
    }

    if (text == "error reading measurement")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(传感器)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "SHT3X 传感器读取失败", Color = "#EF5350", Severity = FieldSeverity.Error });
        return true;
    }

    return false;
}

// ---- 按键 ----

bool TryParseKeyEvent(List<FieldAnnotation> f, string text)
{
    // KEY 0x%08x, TYPE %s.
    var m = Regex.Match(text, @"^KEY 0x([0-9A-Fa-f]{8}), TYPE (.+)\.");
    if (m.Success)
    {
        f.Add(new FieldAnnotation { Name = "日志类别(按键)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "按键事件", Color = "#3478F6" });
        f.Add(new FieldAnnotation { Name = "键值", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"0x{m.Groups[1].Value}" });
        f.Add(new FieldAnnotation { Name = "类型", Offset = 0, Length = 0, RawHex = "", DisplayValue = m.Groups[2].Value });
        return true;
    }

    return false;
}

// ---- GATT 客户端事件 ----

bool TryParseGattEvent(List<FieldAnnotation> f, string text)
{
    if (text == "peer svc discovery done")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(GATT)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "对端服务发现完成", Color = "#22C55E" });
        return true;
    }

    return false;
}

// ---- ESAIR CCC ----

bool TryParseEsairCcc(List<FieldAnnotation> f, string text)
{
    // ESAIR CCC notify %s  / ESAIR TX CCC notify %s
    var m = Regex.Match(text, @"^(ESAIR(?:\s+\w+)?\s+CCC)\s+notify\s+(EN|DIS)");
    if (m.Success)
    {
        var ch = m.Groups[1].Value;
        var en = m.Groups[2].Value;
        f.Add(new FieldAnnotation { Name = "日志类别(ESAIR)", Offset = 0, Length = 0, RawHex = "", DisplayValue = $"{ch} CCCD 通知", Color = "#3478F6" });
        f.Add(new FieldAnnotation { Name = "状态", Offset = 0, Length = 0, RawHex = "", DisplayValue = en == "EN" ? "已使能" : "已禁能", Color = en == "EN" ? "#22C55E" : "#888888" });
        return true;
    }

    return false;
}

// ---- OTA ----

bool TryParseOta(List<FieldAnnotation> f, string text)
{
    if (text == "ntf_enable:true" || text == "ntf_enable:false")
    {
        var en = text.Contains("true");
        f.Add(new FieldAnnotation { Name = "日志类别(OTA)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "OTA 通知使能状态", Color = "#3478F6" });
        f.Add(new FieldAnnotation { Name = "状态", Offset = 0, Length = 0, RawHex = "", DisplayValue = en ? "已使能" : "已禁能" });
        return true;
    }

    return false;
}

// ---- IOT→MQTT 配置转换 ----

bool TryParseIotToMqtt(List<FieldAnnotation> f, string text)
{
    // iot_to_mqtt: applied successfully
    if (text == "iot_to_mqtt: applied successfully")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(IOT→MQTT)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "IoT→MQTT 配置转换成功", Color = "#22C55E" });
        return true;
    }

    // iot_to_mqtt: IOT config incomplete, skip
    if (text == "iot_to_mqtt: IOT config incomplete, skip")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(IOT→MQTT)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "IoT 配置不完整，跳过转换", Color = "#FF9800", Severity = FieldSeverity.Warning });
        return true;
    }

    // iot_to_mqtt: mqtt_config_save FAILED
    if (text == "iot_to_mqtt: mqtt_config_save FAILED")
    {
        f.Add(new FieldAnnotation { Name = "日志类别(IOT→MQTT)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "IOT→MQTT 配置保存失败", Color = "#EF5350", Severity = FieldSeverity.Error });
        return true;
    }

    // iot_to_mqtt: domain_port format error (no comma)
    if (text.Contains("iot_to_mqtt: domain_port format error"))
    {
        f.Add(new FieldAnnotation { Name = "日志类别(IOT→MQTT)", Offset = 0, Length = 0, RawHex = "", DisplayValue = "域名端口格式错误", Color = "#EF5350", Severity = FieldSeverity.Error });
        return true;
    }

    return false;
}

// ===================================================================
//  二进制帧辅助函数
// ===================================================================

void ParseHeartbeat(List<FieldAnnotation> f, int off, int len)
{
    int expected = 12;
    if (len < expected)
    {
        f.Add(new FieldAnnotation { Name = "数据(异常)", Offset = off, Length = len, RawHex = RawHex(off, len), DisplayValue = $"心跳数据长度不足{expected}字节", Color = "#EF5350", Severity = FieldSeverity.Error });
        return;
    }

    var power = ToFloat(off);
    f.Add(new FieldAnnotation { Name = "功率(Power)", Offset = off, Length = 4, RawHex = RawHex(off, 4), DisplayValue = $"{power:F1}W", Color = "#FF9800" });
    off += 4;

    var temp = ToFloat(off);
    f.Add(new FieldAnnotation { Name = "温度(Temp)", Offset = off, Length = 4, RawHex = RawHex(off, 4), DisplayValue = $"{temp:F1}°C", Color = "#FF9800" });
    off += 4;

    var status = RawData[off];
    f.Add(new FieldAnnotation { Name = "开关状态(Status)", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = status == 1 ? "ON" : "OFF", Color = status == 1 ? "#22C55E" : "#888888" });
    off++;

    var mode = RawData[off];
    string modeName = mode switch { 0 => "Auto", 1 => "Cold", 2 => "Hot", 3 => "Dry", 4 => "Fan", 5 => "Sleep", _ => $"Unknown({mode})" };
    f.Add(new FieldAnnotation { Name = "模式(Mode)", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = $"{mode} ({modeName})", Color = mode <= 5 ? "#3478F6" : "#FF9800", Severity = mode <= 5 ? FieldSeverity.Normal : FieldSeverity.Warning });
    off++;

    var setTemp = RawData[off];
    bool tempOk = setTemp >= 16 && setTemp <= 30;
    f.Add(new FieldAnnotation { Name = "设定温度(SetTemp)", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = $"{setTemp}°C", Color = tempOk ? "#3478F6" : "#FF9800", Severity = tempOk ? FieldSeverity.Normal : FieldSeverity.Warning });
    off++;

    var wind = RawData[off];
    f.Add(new FieldAnnotation { Name = "风速(Wind)", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = $"{wind}" });
    off++;

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

    int remaining = RawData.Length - off - 3;
    if (remaining <= 0) return;

    if (cmdType == "ctrl")
        ParseCtrlParams(f, off, remaining, sub);
    else if (cmdType == "modify")
        ParseModifyParams(f, off, remaining, sub);
    else
        f.Add(new FieldAnnotation { Name = $"{prefix}参数", Offset = off, Length = remaining, RawHex = RawHex(off, remaining), DisplayValue = $"{remaining} 字节", Color = "#888888" });
}

void ParseCtrlParams(List<FieldAnnotation> f, int off, int len, byte sub)
{
    switch (sub)
    {
        case 0x01:
            if (len >= 1)
            {
                var val = RawData[off];
                f.Add(new FieldAnnotation { Name = "开关", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = val == 1 ? "ON (开机)" : "OFF (关机)", Color = val == 1 ? "#22C55E" : "#888888" });
            }
            break;
        case 0x02:
            if (len >= 1)
            {
                var mode = RawData[off];
                string modeName = mode switch { 0 => "Auto (自动)", 1 => "Cold (制冷)", 2 => "Hot (制热)", 3 => "Dry (除湿)", 4 => "Fan (送风)", 5 => "Sleep (睡眠)", _ => $"未知({mode})" };
                f.Add(new FieldAnnotation { Name = "模式", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = modeName, Color = mode <= 5 ? "#3478F6" : "#FF9800", Severity = mode <= 5 ? FieldSeverity.Normal : FieldSeverity.Warning });
            }
            break;
        case 0x03:
            if (len >= 1)
            {
                var temp = RawData[off];
                bool tempOk = temp >= 16 && temp <= 30;
                f.Add(new FieldAnnotation { Name = "设定温度", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = $"{temp}°C", Color = tempOk ? "#3478F6" : "#FF9800", Severity = tempOk ? FieldSeverity.Normal : FieldSeverity.Warning });
            }
            break;
        case 0x04:
            if (len >= 1)
            {
                var wind = RawData[off];
                string windName = wind switch { 0 => "自动", 1 => "低速", 2 => "中速", 3 => "高速", _ => $"未知({wind})" };
                f.Add(new FieldAnnotation { Name = "风速", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = $"{wind} ({windName})", Color = "#3478F6" });
            }
            break;
        case 0x07: case 0x08: case 0x09: case 0x0A:
            if (len >= 1)
            {
                var keyIdx = RawData[off];
                f.Add(new FieldAnnotation { Name = "按键索引", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = $"{keyIdx}" });
            }
            break;
        case 0x0B:
            if (len >= 1)
            {
                var val = RawData[off];
                f.Add(new FieldAnnotation { Name = "监听开关", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = val == 1 ? "开启" : "关闭", Color = val == 1 ? "#22C55E" : "#888888" });
            }
            break;
        case 0x0C:
            if (len >= 1)
                f.Add(new FieldAnnotation { Name = "监听模式", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = $"模式 {RawData[off]}" });
            break;
        case 0x0D:
            if (len >= 7)
            {
                var year = ToUInt16(off);
                f.Add(new FieldAnnotation { Name = "时间", Offset = off, Length = 7, RawHex = RawHex(off, 7), DisplayValue = $"{year}-{RawData[off+2]:D2}-{RawData[off+3]:D2} {RawData[off+4]:D2}:{RawData[off+5]:D2}:{RawData[off+6]:D2}", Color = "#3478F6" });
            }
            break;
        case 0x0E:
            if (len >= 1)
            {
                var nameLen = RawData[off];
                var maxLen = Math.Min(nameLen, len - 1);
                var name = System.Text.Encoding.ASCII.GetString(RawData, off + 1, maxLen);
                f.Add(new FieldAnnotation { Name = "BLE名称", Offset = off, Length = 1 + maxLen, RawHex = RawHex(off, 1 + maxLen), DisplayValue = name, Color = "#3478F6" });
            }
            break;
        case 0x0F:
            if (len >= 2)
                f.Add(new FieldAnnotation { Name = "设备地址", Offset = off, Length = 2, RawHex = RawHex(off, 2), DisplayValue = $"{ToUInt16(off)}", Color = "#3478F6" });
            break;
        case 0x10:
            if (len >= 2)
                f.Add(new FieldAnnotation { Name = "定时时间", Offset = off, Length = 2, RawHex = RawHex(off, 2), DisplayValue = $"{ToUInt16(off)} 分钟", Color = "#3478F6" });
            break;
        case 0x11:
            if (len >= 14) ParseTimerData(f, off, 14);
            break;
        case 0x12:
            ParseMqttTlv(f, off, len);
            break;
        default:
            if (len > 0)
                f.Add(new FieldAnnotation { Name = "控制参数", Offset = off, Length = len, RawHex = RawHex(off, len), DisplayValue = $"{len} 字节", Color = "#888888" });
            break;
    }
}

void ParseModifyParams(List<FieldAnnotation> f, int off, int len, byte sub)
{
    switch (sub)
    {
        case 0x13: case 0x14: case 0x15: case 0x18: case 0x19:
            if (len >= 2)
            {
                var totalPkgs = RawData[off];
                var pkgNo = RawData[off + 1];
                f.Add(new FieldAnnotation { Name = "总包数", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = $"{totalPkgs}" });
                f.Add(new FieldAnnotation { Name = "当前包号", Offset = off + 1, Length = 1, RawHex = RawHex(off + 1, 1), DisplayValue = $"{pkgNo}/{totalPkgs}" });
                if (len > 2)
                {
                    var dataBytes = Math.Min(5, len - 2);
                    var ascii = System.Text.Encoding.ASCII.GetString(RawData, off + 2, dataBytes).TrimEnd('\0');
                    f.Add(new FieldAnnotation { Name = "分包数据", Offset = off + 2, Length = dataBytes, RawHex = RawHex(off + 2, dataBytes), DisplayValue = string.IsNullOrEmpty(ascii) ? $"{dataBytes} 字节" : ascii, Color = "#888888" });
                }
            }
            break;
        default:
            if (len > 0)
                f.Add(new FieldAnnotation { Name = "修改参数", Offset = off, Length = len, RawHex = RawHex(off, len), DisplayValue = $"{len} 字节", Color = "#888888" });
            break;
    }
}

void ParseTimerData(List<FieldAnnotation> f, int off, int len)
{
    if (len < 14) return;
    var enabled = RawData[off];
    f.Add(new FieldAnnotation { Name = "定时开关", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = enabled == 1 ? "已启用" : "已禁用", Color = enabled == 1 ? "#22C55E" : "#888888" });
    var mode = RawData[off + 1];
    string modeName = mode switch { 0 => "Auto", 1 => "Cold", 2 => "Hot", 3 => "Dry", 4 => "Fan", 5 => "Sleep", _ => $"?({mode})" };
    f.Add(new FieldAnnotation { Name = "定时模式", Offset = off + 1, Length = 1, RawHex = RawHex(off + 1, 1), DisplayValue = modeName });
    f.Add(new FieldAnnotation { Name = "定时温度", Offset = off + 2, Length = 1, RawHex = RawHex(off + 2, 1), DisplayValue = $"{RawData[off + 2]}°C" });
    f.Add(new FieldAnnotation { Name = "定时风速", Offset = off + 3, Length = 1, RawHex = RawHex(off + 3, 1), DisplayValue = $"{RawData[off + 3]}" });
    var remainMin = ToUInt16(off + 4);
    f.Add(new FieldAnnotation { Name = "倒计时剩余", Offset = off + 4, Length = 2, RawHex = RawHex(off + 4, 2), DisplayValue = $"{remainMin} 分钟" });
    var timerMin = ToUInt16(off + 6);
    f.Add(new FieldAnnotation { Name = "定时关时间", Offset = off + 6, Length = 2, RawHex = RawHex(off + 6, 2), DisplayValue = $"{timerMin} 分钟" });
    var weekBitmap = RawData[off + 8];
    string weekStr = "";
    string[] weekNames = { "一", "二", "三", "四", "五", "六", "日" };
    for (int i = 0; i < 7; i++)
        if ((weekBitmap & (1 << i)) != 0) weekStr += (weekStr.Length > 0 ? "," : "") + weekNames[i];
    f.Add(new FieldAnnotation { Name = "重复星期", Offset = off + 8, Length = 1, RawHex = RawHex(off + 8, 1), DisplayValue = string.IsNullOrEmpty(weekStr) ? "不重复" : $"周{weekStr}", Color = "#3478F6" });
    f.Add(new FieldAnnotation { Name = "开始时间", Offset = off + 9, Length = 2, RawHex = RawHex(off + 9, 2), DisplayValue = $"{RawData[off + 9]:D2}:{RawData[off + 10]:D2}" });
    f.Add(new FieldAnnotation { Name = "结束时间", Offset = off + 11, Length = 2, RawHex = RawHex(off + 11, 2), DisplayValue = $"{RawData[off + 11]:D2}:{RawData[off + 12]:D2}" });
}

void ParseMqttTlv(List<FieldAnnotation> f, int off, int len)
{
    int pos = off;
    int end = off + len;
    int tlvCount = 0;
    while (pos < end - 1)
    {
        var tag = RawData[pos];
        if (pos + 1 >= end) break;
        var length = RawData[pos + 1];
        if (pos + 2 + length > end) break;
        string tagName = tag switch
        {
            0x01 => "服务器地址",
            0x02 => "端口",
            0x03 => "客户端ID",
            0x04 => "用户名",
            0x05 => "密码",
            0x06 => "订阅主题",
            0x07 => "发布主题",
            0x08 => "订阅主题2",
            _ => $"未知Tag(0x{tag:X2})"
        };
        var value = System.Text.Encoding.ASCII.GetString(RawData, pos + 2, length).TrimEnd('\0');
        f.Add(new FieldAnnotation { Name = $"TLV:{tagName}", Offset = pos, Length = 2 + length, RawHex = RawHex(pos, 2 + length), DisplayValue = value, Color = "#8B5CF6" });
        pos += 2 + length;
        tlvCount++;
    }
    if (tlvCount == 0)
        f.Add(new FieldAnnotation { Name = "MQTT配置", Offset = off, Length = len, RawHex = RawHex(off, len), DisplayValue = "(空)", Color = "#888888" });
}

int CountBits(ushort val)
{
    int count = 0;
    while (val != 0) { count++; val &= (ushort)(val - 1); }
    return count;
}

void ParseCtrlResp(List<FieldAnnotation> f, int off, int len)
{
    if (len < 5)
    {
        f.Add(new FieldAnnotation { Name = "数据(异常)", Offset = off, Length = len, RawHex = RawHex(off, len), DisplayValue = "控制回复数据长度不足", Color = "#EF5350", Severity = FieldSeverity.Error });
        return;
    }

    var ctrlObj = RawData[off];
    string ctrlObjName = GetSubCmdName("ctrl", ctrlObj);
    f.Add(new FieldAnnotation { Name = "控制对象", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = $"0x{ctrlObj:X2} ({ctrlObjName})", Color = "#3478F6" });
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
        case 0x01:
            if (dataLen >= 14)
            {
                var addr = BitConverter.ToString(RawData, off, 6).Replace("-", ":");
                f.Add(new FieldAnnotation { Name = "设备ID", Offset = off, Length = 6, RawHex = RawHex(off, 6), DisplayValue = addr, Color = "#888888" });
                var fw = (uint)(RawData[off + 6] | (RawData[off + 7] << 8) | (RawData[off + 8] << 16) | (RawData[off + 9] << 24));
                f.Add(new FieldAnnotation { Name = "固件版本", Offset = off + 6, Length = 4, RawHex = RawHex(off + 6, 4), DisplayValue = $"0x{fw:X8}" });
                var hw = (uint)(RawData[off + 10] | (RawData[off + 11] << 8) | (RawData[off + 12] << 16) | (RawData[off + 13] << 24));
                f.Add(new FieldAnnotation { Name = "硬件版本", Offset = off + 10, Length = 4, RawHex = RawHex(off + 10, 4), DisplayValue = $"0x{hw:X8}" });
            }
            break;
        case 0x02:
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
        case 0x03:
            if (dataLen >= 4)
            {
                var pwr = ToFloat(off);
                f.Add(new FieldAnnotation { Name = "功率", Offset = off, Length = 4, RawHex = RawHex(off, 4), DisplayValue = $"{pwr:F1}W", Color = "#FF9800" });
            }
            break;
        case 0x04:
            if (dataLen >= 4)
            {
                var tmp = ToFloat(off);
                f.Add(new FieldAnnotation { Name = "温度", Offset = off, Length = 4, RawHex = RawHex(off, 4), DisplayValue = $"{tmp:F1}°C", Color = "#FF9800" });
            }
            break;
        case 0x05:
            if (dataLen >= 4)
            {
                f.Add(new FieldAnnotation { Name = "功率ADC", Offset = off, Length = 2, RawHex = RawHex(off, 2), DisplayValue = $"{ToUInt16(off)}" });
                f.Add(new FieldAnnotation { Name = "NTC ADC", Offset = off + 2, Length = 2, RawHex = RawHex(off + 2, 2), DisplayValue = $"{ToUInt16(off + 2)}" });
            }
            break;
        case 0x06:
            if (dataLen >= 1)
            {
                var nameLen = RawData[off];
                var maxLen = Math.Min(nameLen, dataLen - 1);
                var name = System.Text.Encoding.ASCII.GetString(RawData, off + 1, maxLen);
                f.Add(new FieldAnnotation { Name = "BLE名称", Offset = off, Length = 1 + maxLen, RawHex = RawHex(off, 1 + maxLen), DisplayValue = name, Color = "#888888" });
            }
            break;
        case 0x07:
            if (dataLen >= 2)
                f.Add(new FieldAnnotation { Name = "设备地址", Offset = off, Length = 2, RawHex = RawHex(off, 2), DisplayValue = $"{ToUInt16(off)}" });
            break;
        case 0x08:
            if (dataLen >= 14) ParseTimerData(f, off, 14);
            break;
        case 0x09:
            ParseMqttTlv(f, off, dataLen);
            break;
        case 0x0A:
            if (dataLen >= 4)
            {
                var listenSw = RawData[off];
                f.Add(new FieldAnnotation { Name = "监听开关", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = listenSw == 1 ? "已开启" : "已关闭", Color = listenSw == 1 ? "#22C55E" : "#888888" });
                var listenMd = RawData[off + 1];
                f.Add(new FieldAnnotation { Name = "监听模式", Offset = off + 1, Length = 1, RawHex = RawHex(off + 1, 1), DisplayValue = $"模式 {listenMd}" });
                var keyBitmap = ToUInt16(off + 2);
                f.Add(new FieldAnnotation { Name = "按键位图", Offset = off + 2, Length = 2, RawHex = RawHex(off + 2, 2), DisplayValue = $"0x{keyBitmap:X4} ({CountBits(keyBitmap)}个已学习)" });
            }
            break;
        case 0x0B:
            if (dataLen >= 4)
            {
                var learnStatus = RawData[off];
                f.Add(new FieldAnnotation { Name = "学习状态", Offset = off, Length = 1, RawHex = RawHex(off, 1), DisplayValue = learnStatus == 1 ? "已学习" : "未学习", Color = learnStatus == 1 ? "#22C55E" : "#888888" });
                var carrierFreq = ToUInt16(off + 1);
                f.Add(new FieldAnnotation { Name = "载波频率", Offset = off + 1, Length = 2, RawHex = RawHex(off + 1, 2), DisplayValue = $"{carrierFreq} Hz" });
                var dataPoints = RawData[off + 3];
                f.Add(new FieldAnnotation { Name = "数据点数", Offset = off + 3, Length = 1, RawHex = RawHex(off + 3, 1), DisplayValue = $"{dataPoints}" });
                if (dataLen > 4)
                    f.Add(new FieldAnnotation { Name = "脉冲数据", Offset = off + 4, Length = dataLen - 4, RawHex = RawHex(off + 4, Math.Min(16, dataLen - 4)), DisplayValue = $"{dataLen - 4} 字节 (红外码)", Color = "#EC4899" });
            }
            break;
        case 0x0C:
        case 0x0D:
        case 0x0E:
        case 0x0F:
        case 0x10:
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
