# 协议解析

ACCCOM 提供了一套完整的协议解析体系，从脚本驱动的 C# 解析引擎到可视化编辑器、多帧拼接重组和协议测试运行器，实现从"看原始 Hex"到"看懂数据含义"的完整闭环。

> 📖 从协议文档到解析器的完整操作流程，请参阅 **[协议→解析器 操作指南](../protocol-to-parser.md)**，包含 JSON Schema 自动生成、手写脚本、MCP 调用三种路径。

## 协议解析引擎（脚本驱动）

基于 Roslyn C# Script，用户根据协议文档编写 `.csx` 解析脚本，串口数据到达后自动解析为结构化字段。

### 协议解析器生成器

`ParserGenerator` 从 `ProtocolSchema` 自动生成 `.csx` 解析器代码，支持：

- **字段类型**：hex、uint8/16/32、int8/16/32、float、double、string、BCD、enum、bitfield
- **帧结构验证**：自动验证帧头、帧尾、最小长度
- **校验和验证**：CRC16（Modbus/CCITT）、Sum8、Xor8、Sum16
- **命令码分发**：根据命令码自动分发到不同解析逻辑
- **字节序支持**：大端/小端序配置
- **枚举映射**：枚举值和位域解析

### 脚本模板

```csharp
// my_device.csx
// 输入: RawData(byte[]), Timestamp(DateTime)
// 输出: List<FieldAnnotation>

var result = new List<FieldAnnotation>();
result.Add(new FieldAnnotation {
    Name = "温度", Offset = 4, Length = 1,
    RawHex = RawHex(4, 1),
    DisplayValue = $"{RawData[4]} °C",
    Severity = RawData[4] > 80 ? FieldSeverity.Warning : FieldSeverity.Normal
});
return result;
```

### Modbus RTU 模板

```csharp
// modbus_rtu_template.csx
// Modbus RTU 帧解析模板

var fields = new List<FieldAnnotation>();

// 帧头验证
var header = ToUInt16(0, false);
bool headerOk = header == 0x0001; // 从站地址
fields.Add(new FieldAnnotation {
    Name = "从站地址", Offset = 0, Length = 1,
    RawHex = RawHex(0, 1),
    DisplayValue = $"0x{RawData[0]:X2}",
    Color = headerOk ? "#22C55E" : "#EF5350",
    Severity = headerOk ? FieldSeverity.Normal : FieldSeverity.Error
});

// 功能码
var functionCode = RawData[1];
string funcName = functionCode switch {
    0x01 => "读线圈",
    0x02 => "读离散输入",
    0x03 => "读保持寄存器",
    0x04 => "读输入寄存器",
    0x05 => "写单个线圈",
    0x06 => "写单个寄存器",
    _ => $"未知(0x{functionCode:X2})"
};
fields.Add(new FieldAnnotation {
    Name = "功能码", Offset = 1, Length = 1,
    RawHex = RawHex(1, 1),
    DisplayValue = $"0x{functionCode:X2} ({funcName})"
});

// CRC16 校验
var receivedCs = ToUInt16(RawData.Length - 2, false);
var calcCs = Crc16(0, RawData.Length - 2);
bool csOk = receivedCs == calcCs;
fields.Add(new FieldAnnotation {
    Name = "CRC16", Offset = RawData.Length - 2, Length = 2,
    RawHex = RawHex(RawData.Length - 2, 2),
    DisplayValue = csOk ? $"0x{receivedCs:X4} (校验通过)" : $"0x{receivedCs:X4} (计算值=0x{calcCs:X4})",
    Color = csOk ? "#22C55E" : "#EF5350",
    Severity = csOk ? FieldSeverity.Normal : FieldSeverity.Error
});

return fields;
```

### 辅助函数

内置辅助函数定义在 `ScriptGlobals.cs` 中：`RawHex`、`ToUInt16`、`ToInt16`、`ToUInt32`、`ToInt32`、`ToFloat`、`ToDouble`、`FromBcd`、`Crc16`、`Sum8`、`Xor8`、`Sum16`

### 脚本特性

- 脚本热加载：修改 `.csx` 文件后即时生效
- 离线解析：不连串口也可直接解析 Hex 数据
- UI 中选中条目即可查看字段树
- 支持从 `ProtocolSchema` 自动生成解析器代码

## 协议可视化编辑器

`SchemaEditorWindow` 提供图形化字段编辑界面，无需手写 JSON 或代码：

- 填写协议参数（帧头、帧尾、校验类型）
- DataGrid 逐行编辑字段（名称、偏移、长度、类型、单位、大端、枚举值）
- 自动生成 JSON Schema 和 `.csx` 脚本（通过 `ParserGenerator`）
- 内置 Test Parse 面板，输入 Hex 即可测试
- 支持加载内置模板，快速开始
- 支持命令码分发配置（不同命令码解析不同字段）

## 多帧拼接重组

`FrameAssembler` 将分片数据重组为完整帧后再解析：

- 帧头匹配 + 长度字段判断完整帧
- 超时检测自动丢弃不完整帧
- 组装后自动执行解析器
- 分片数据不污染 RX 列表
- 桌面 UI 配置窗口（工具栏 🔗 按钮）

## 协议测试运行器

`ProtocolTestRunner` 执行结构化的协议测试脚本（`TestScript` 模型）：

- 定义测试步骤：每步包含命令、超时、预期响应模式
- 自动匹配验证：contains / regex / exact
- 输出测试报告：逐步 pass/fail 结果、执行时间、错误详情
- 支持 `send_batch` MCP 工具批量发送命令序列
- 支持 Modbus 命令测试

## Related

- [协议→解析器 操作指南](../protocol-to-parser.md)
- [Modbus 支持](../guide/modbus.md)
- [高级通信功能](../guide/advanced-comms.md)
- [串口操作指南](../guide/serial.md)
