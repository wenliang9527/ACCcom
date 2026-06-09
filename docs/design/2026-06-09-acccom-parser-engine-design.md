# ACCCOM 协议解析引擎设计

## 概述

为 ACCCOM 串口调试工具增加脚本驱动的协议解析能力。用户根据硬件协议文档编写 C# Script 解析脚本，接收串口数据后自动解析并标注字段含义，解决高频手动拼报文、看原始 Hex 低效的问题。

## 核心接口

### 脚本输入（由引擎传入）

```csharp
byte[] RawData   // 收到的原始字节
DateTime Time    // 时间戳
```

### 脚本输出（用户脚本构造并返回）

```csharp
class FieldAnnotation
{
    string Name          // 字段名称，如"温度"
    int Offset           // 起始字节偏移
    int Length           // 字节长度
    string RawHex        // 原始 Hex 字符串
    string DisplayValue  // 解析后的可读值
    string? Color        // 可选高亮色
    FieldSeverity Severity  // Normal | Warning | Error
}
```

### 脚本模板

```csharp
// parsers/my_device.csx
// 协议文档: docs/my-device-protocol.pdf
// 帧头: AA 55  长度: byte[2]  校验: CRC16

var result = new List<FieldAnnotation>();

if (RawData.Length < 4) return result;

result.Add(new("帧头", 0, 2, RawHex(0,2), "AA 55", "#888888"));
result.Add(new("长度", 2, 1, RawHex(2,1), $"{RawData[2]} 字节"));
result.Add(new("温度", 4, 1, RawHex(4,1), $"{RawData[4]} °C"));

return result;
```

### 引擎内置辅助函数（脚本中可直接调用）

```
RawHex(offset, length)       → string  取指定范围的 Hex
ToUInt16(offset, bigEndian)  → ushort
ToInt16(offset, bigEndian)   → short
Crc16(offset, length)        → ushort  计算 CRC16
Sum8(offset, length)         → byte    和校验
Xor8(offset, length)         → byte    异或校验
```

## 文件管理

- 解析脚本存储于 `parsers/` 目录
- 每个脚本一个 `.csx` 文件
- 用户通过 UI 下拉选择当前生效的解析器
- 支持在 UI 中打开脚本目录、刷新脚本列表

## 架构

```
SerialService.OnDataReceived
    → 原始 LogEntry
        → 当前有激活解析器？
            → 是: 引擎调用脚本 → List<FieldAnnotation>
            → 否: 保持原始显示
        → 渲染 UI
```

### 新文件清单

```
Services/
  ParserEngine.cs          — 脚本加载、编译、调用
  ParserManager.cs         — 解析器列表管理、激活切换

Models/
  FieldAnnotation.cs       — 字段标注数据结构

ViewModels/
  ParserViewModel.cs       — 解析器相关 UI 状态

Converters/
  SeverityToColorConverter — 严重级别→颜色
```

### 改动文件

```
ViewModels/MainViewModel.cs   — 集成 ParserManager，数据流增加解析步骤
MainWindow.xaml               — 新增字段解析面板、解析器选择器
```

### 脚本引擎选型

使用 **Roslyn (Microsoft.CodeAnalysis.CSharp.Scripting)**：
- .NET 原生，零额外运行时依赖
- 脚本可引用项目内任意类型
- 热加载：每次调用独立编译上下文，修改脚本后即时生效
- 可通过 `ScriptOptions` 限制脚本能力（沙箱可选）

## UI 展示

### 列表行
RX 记录增加颜色标记条，根据 Severity 着色：
- Normal → 默认
- Warning → 黄色标记
- Error → 红色标记 + 行背景高亮

### 字段解析面板（选中记录时展示）
树形结构展示每个字段：名称、原始 Hex、解析值。
点击字段在 Hex 视图中高亮对应字节。

### Hex 字节着色
原始 Hex 字符串底部用圆点/标记表示字段范围，颜色对应字段类别。

## 后续扩展预留

- **自动化脚本**：解析引擎输出的字段值可作为条件触发（值 > 阈值 → 自动发送响应指令）
- **数据导出**：字段名 + 解析值为列名，直接导出 CSV/JSON
- **图表监控**：选取数值型字段（如温度）自动绘制实时趋势图
- **多帧拼接**：引擎可缓存分片数据，待完整帧到达后再解析

此三项均依赖解析引擎输出的结构化字段数据，引擎先行。

## 不做的事情

- 不提供可视化协议编辑器（用脚本编辑器+模板）
- 不内置具体设备协议库（用户按文档写脚本）
- 不做实时波形（留给后续图表功能）
