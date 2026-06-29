# 解析器开发指南

## 目录结构

```
parsers/
├── your_protocol.csx          # 解析器脚本（必需）
├── your_protocol.schema.json  # 自动匹配配置（可选，用于智能匹配）
└── ...
```

## 创建新解析器

### 步骤 1：编写 .csx 解析器脚本

创建 `your_protocol.csx`，模板如下：

```csharp
var fields = new List<FieldAnnotation>();

// 1. 帧长度检查
if (RawData.Length < 最小帧长度)
{
    fields.Add(new FieldAnnotation {
        Name = "错误",
        Offset = 0,
        Length = RawData.Length,
        RawHex = RawHex(0, RawData.Length),
        DisplayValue = "帧长度不足",
        Color = "#EF5350",
        Severity = FieldSeverity.Error
    });
    return fields;
}

// 2. 帧头验证
var header = ToUInt16(0, true);
bool headerOk = header == 0xA55A;
fields.Add(new FieldAnnotation {
    Name = "帧头",
    Offset = 0,
    Length = 2,
    RawHex = RawHex(0, 2),
    DisplayValue = headerOk ? "A5 5A (正确)" : $"0x{header:X4} (错误)",
    Color = headerOk ? "#22C55E" : "#EF5350",
    Severity = headerOk ? FieldSeverity.Normal : FieldSeverity.Error
});

// 3. 解析各字段
var fieldValue = ToUInt16(offset, bigEndian);
fields.Add(new FieldAnnotation {
    Name = "字段名",
    Offset = offset,
    Length = 2,
    RawHex = RawHex(offset, 2),
    DisplayValue = $"{fieldValue}",
    Color = "#3478F6"
});

// 4. 校验和验证
var receivedCs = ToUInt16(RawData.Length - 2, false);
var calcCs = Crc16(0, RawData.Length - 2);
bool csOk = receivedCs == calcCs;
fields.Add(new FieldAnnotation {
    Name = "CRC16",
    Offset = RawData.Length - 2,
    Length = 2,
    RawHex = RawHex(RawData.Length - 2, 2),
    DisplayValue = csOk ? $"0x{receivedCs:X4} (正确)" : $"0x{receivedCs:X4} (计算=0x{calcCs:X4})",
    Color = csOk ? "#22C55E" : "#EF5350",
    Severity = csOk ? FieldSeverity.Normal : FieldSeverity.Error
});

return fields;
```

### 可用 API

| 方法 | 说明 |
|------|------|
| `ToUInt16(offset, bigEndian?)` | 读取 16 位无符号整数 |
| `ToUInt32(offset, bigEndian?)` | 读取 32 位无符号整数 |
| `ToInt16(offset, bigEndian?)` | 读取 16 位有符号整数 |
| `ToFloat(offset, bigEndian?)` | 读取单精度浮点数 |
| `RawHex(offset, length)` | 获取指定范围的十六进制字符串 |
| `Sum8(offset, length)` | 8 位校验和 |
| `Sum16(offset, length)` | 16 位校验和 |
| `Xor8(offset, length)` | 8 位异或校验 |
| `Crc16(offset, length)` | CRC16-Modbus 校验 |
| `Crc16Ccitt(offset, length)` | CRC16-CCITT 校验 |
| `FromBcd(offset, length)` | BCD 解码 |

### 字段属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `Name` | string | 字段名称（必填） |
| `Offset` | int | 字节偏移（必填） |
| `Length` | int | 字节长度（必填） |
| `RawHex` | string | 原始十六进制 |
| `DisplayValue` | string | 显示值（必填） |
| `Color` | string | 颜色（十六进制，如 `#FF0000`） |
| `Severity` | FieldSeverity | 严重度：Normal / Warning / Error |

---

## 创建 .schema.json 配置（可选）

用于**智能匹配解析器**，当数据到达时自动选择最合适的解析器。

### 手动创建

```json
{
  "name": "esoac_v3",
  "description": "ESOAC V3 协议",
  "minLength": 9,
  "autoMatch": {
    "enabled": true,
    "priority": 10,
    "headerPattern": "A5 5A",
    "commandOffset": 4,
    "knownCommands": ["0x03", "0x05", "0x85", "0x87"]
  },
  "frame": {
    "header": "A5 5A"
  }
}
```

### 自动提取

```powershell
.\tools\generate_schemas.ps1
```

脚本会从 `.csx` 文件中提取帧头特征，生成基础配置文件。

### autoMatch 配置说明

| 字段 | 说明 |
|------|------|
| `enabled` | 是否启用自动匹配 |
| `priority` | 优先级（数值越大越优先），多协议共用帧头时区分用 |
| `headerPattern` | 帧头十六进制，如 `"A5 5A"` |
| `commandOffset` | 命令码偏移（可选，用于多命令协议） |
| `knownCommands` | 已知命令码列表（可选） |

---

## 数据流

```
串口原始字节
    ↓
SerialService.OnSerialDataReceived
    ↓ 生成 LogEntry (RawHex = 原始字节的十六进制)
    ↓
DataFlowViewModel.OnSerialData
    ↓ 显示原始日志条目
    ↓ HexHelper.HexStringToBytes → 原始字节
    ↓
FrameBuffer.Write
    ↓ 环形缓冲区累积
    ↓ FindHeader 扫描帧头 (A5 5A)
    ↓ ReadLengthField 读取长度
    ↓ 提取完整帧
    ↓
AutoParserMatcher.MatchParser
    ↓ 根据帧数据特征匹配解析器
    ↓
ParserEngine.ExecuteAsync
    ↓ 执行 .csx 脚本解析
    ↓ 返回 List<FieldAnnotation>
    ↓
OnFrameAssembled 事件
    ↓ 带字段标注的 LogEntry 显示在 UI
```

---

## 帧结构配置

在 `DataFlowViewModel` 中配置 `FrameBufferConfig`：

```csharp
var bufferConfig = new FrameBufferConfig
{
    Strategy = FrameExtractStrategy.ByHeader,  // 按帧头匹配
    Header = new byte[] { 0xA5, 0x5A },        // 帧头字节
    LengthFieldOffset = 2,                       // 长度字段偏移（相对帧头）
    LengthFieldSize = 1,                         // 长度字段字节数 (1 或 2)
    LengthFieldIncludes = 4,                     // 帧头+长度字节+帧尾等固定开销
    MaxFrameSize = 4096,                         // 最大帧长度
    BufferCapacity = 65536,                      // 缓冲区容量
    PartialFrameTimeoutMs = 2000                 // 不完整帧超时（ms）
};
```

### LengthFieldIncludes 计算

`LengthFieldIncludes` = 帧头(2) + 长度字节本身(1) + 帧尾(1) = 4

计算公式：`总帧长度 = LengthFieldValue + LengthFieldIncludes`

---

## 添加新协议示例

假设要添加 `my_device` 协议：

**帧结构：** `[帧头 AA 55 (2)] [长度 (1)] [命令 (1)] [数据 (N)] [校验 (1)]`

1. 创建 `parsers/my_device.csx` - 编写解析逻辑
2. 创建 `parsers/my_device.schema.json`：

```json
{
  "name": "my_device",
  "description": "我的设备协议",
  "minLength": 5,
  "autoMatch": {
    "enabled": true,
    "priority": 5,
    "headerPattern": "AA 55"
  },
  "frame": {
    "header": "AA 55"
  }
}
```

3. 重启应用，选择 `my_device` 解析器

---

## 调试

- 在 VS Output 窗口查看 `Debug.WriteLine` 输出
- 通过 HTTP API 检查数据：`http://127.0.0.1:8899/api/data?since=0&limit=5`
- 通过 HTTP API 检查解析器：`http://127.0.0.1:8899/api/parsers`
