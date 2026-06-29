# 自动标注设计方案

## 1. 需求分析

### 用户需求
1. **智能匹配解析器** - 根据接收到的数据特征自动选择最合适的解析器
2. **实时字段展示** - 数据到达时立即显示字段标注信息
3. **不被时间戳打断** - 解决帧数据被分割导致无法正确匹配的问题

### 当前问题
- `FrameAssembler` 被动接收 `LogEntry`，但串口数据可能被拆分成多个小块
- 解析器需要手动切换，无法根据数据内容自动选择
- TX 数据不会自动解析标注

## 2. 架构设计

### 2.1 整体架构

```
串口数据流
    ↓
SerialService (原始字节)
    ↓
FrameBuffer (新增 - 字节级缓冲，按帧边界切分)
    ↓
AutoParserMatcher (新增 - 智能匹配解析器)
    ↓
ParserEngine (执行解析)
    ↓
LogEntry (带 Fields 标注)
    ↓
DataFlowViewModel (UI 展示)
```

### 2.2 新增组件

#### FrameBuffer - 帧缓冲区
负责将连续的字节流按协议帧边界切分为完整的帧。

```csharp
public class FrameBuffer : IDisposable
{
    private readonly List<byte> _buffer = new();
    private readonly ParserManager _parserManager;
    
    // 事件：组装完成的帧
    public event Action<byte[], LogEntry>? OnFrameReady;
    
    // 喂入原始字节
    public void Feed(byte[] data, LogEntry sourceEntry);
    
    // 尝试从缓冲区提取完整帧
    private void TryExtractFrames();
}
```

#### AutoParserMatcher - 智能解析器匹配
根据数据特征自动选择最合适的解析器。

```csharp
public class AutoParserMatcher
{
    // 解析器指纹缓存
    private readonly Dictionary<string, ParserFingerprint> _fingerprints = new();
    
    // 匹配解析器
    public string? MatchParser(byte[] data);
    
    // 更新指纹缓存
    public void UpdateFingerprint(string parserName, ProtocolSchema schema);
}
```

#### ParserFingerprint - 解析器指纹
描述解析器能识别的数据特征。

```csharp
public class ParserFingerprint
{
    public string? HeaderHex { get; set; }        // 帧头特征
    public int? MinLength { get; set; }           // 最小帧长度
    public int? MaxLength { get; set; }           // 最大帧长度
    public int? CommandOffset { get; set; }       // 命令码偏移
    public byte[]? CommandValues { get; set; }    // 已知命令码
    public int Priority { get; set; }             // 匹配优先级
}
```

### 2.3 数据流改造

#### 当前流程
```
SerialService.OnSerialDataReceived
  → LogEntry (可能不完整)
    → DataFlowViewModel.OnSerialData
      → FrameAssembler.Feed (被动接收)
        → ProcessEntryAsync
          → RunParserAsync (仅 RX，需手动选择解析器)
```

#### 改造后流程
```
SerialService.OnSerialDataReceived
  → 原始字节 + 源 LogEntry
    → FrameBuffer.Feed
      → 自动切分完整帧
        → AutoParserMatcher.MatchParser (自动匹配)
          → ParserEngine.ExecuteAsync
            → 生成带 Fields 的 LogEntry
              → DataFlowViewModel (实时展示)
```

## 3. 详细设计

### 3.1 ProtocolSchema 扩展

在 `ProtocolSchema` 中添加指纹相关属性：

```csharp
public class ProtocolSchema
{
    // 现有属性...
    
    /// <summary>自动匹配配置 (可选)</summary>
    public AutoMatchConfig? AutoMatch { get; set; }
}

public class AutoMatchConfig
{
    /// <summary>是否启用自动匹配</summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>匹配优先级 (数值越大优先级越高)</summary>
    public int Priority { get; set; } = 0;
    
    /// <summary>帧头特征 (十六进制字符串，如 "A5 5A")</summary>
    public string? HeaderPattern { get; set; }
    
    /// <summary>命令码偏移 (用于多命令协议)</summary>
    public int? CommandOffset { get; set; }
    
    /// <summary>已知命令码列表 (十六进制)</summary>
    public string[]? KnownCommands { get; set; }
}
```

### 3.2 ParserManager 增强

```csharp
public class ParserManager
{
    // 新增：指纹缓存
    private readonly Dictionary<string, ParserFingerprint> _fingerprints = new();
    
    // 新增：自动匹配器
    public AutoParserMatcher? AutoMatcher { get; private set; }
    
    // 修改 Activate 方法，更新指纹缓存
    public bool Activate(string? parserName)
    {
        // 现有逻辑...
        
        // 新增：更新指纹
        if (ActiveParserName != null)
        {
            UpdateFingerprint(ActiveParserName, code);
        }
    }
    
    // 新增：匹配解析器
    public string? MatchParser(byte[] data)
    {
        return AutoMatcher?.MatchParser(data);
    }
}
```

### 3.3 FrameBuffer 实现

```csharp
public class FrameBuffer : IDisposable
{
    private readonly byte[] _buffer = new byte[8192];  // 环形缓冲区
    private int _head;
    private int _tail;
    private readonly ParserManager _parserManager;
    private readonly AutoParserMatcher _matcher;
    
    public event Action<LogEntry>? OnFrameAssembled;
    
    public void Feed(byte[] data, LogEntry sourceEntry)
    {
        // 1. 将数据写入缓冲区
        WriteToBuffer(data);
        
        // 2. 尝试提取完整帧
        while (TryExtractFrame(out var frame))
        {
            // 3. 自动匹配解析器
            var parserName = _matcher.MatchParser(frame);
            
            // 4. 执行解析
            var entry = CreateLogEntry(frame, sourceEntry, parserName);
            OnFrameAssembled?.Invoke(entry);
        }
    }
    
    private bool TryExtractFrame(out byte[] frame)
    {
        // 根据帧头/长度字段提取完整帧
        // 支持多种帧格式：
        // - 固定长度帧
        // - 带长度字段的帧
        // - 带帧头帧尾的帧
    }
}
```

### 3.4 DataFlowViewModel 改造

```csharp
public class DataFlowViewModel
{
    private readonly FrameBuffer _frameBuffer;
    
    public DataFlowViewModel(...)
    {
        // 初始化 FrameBuffer
        _frameBuffer = new FrameBuffer(_parserManager);
        _frameBuffer.OnFrameAssembled += OnFrameReady;
    }
    
    public void OnSerialData(LogEntry entry)
    {
        // 将原始数据喂入 FrameBuffer
        var bytes = HexHelper.HexStringToBytes(entry.RawHex);
        _frameBuffer.Feed(bytes, entry);
    }
    
    private void OnFrameReady(LogEntry entry)
    {
        // 解析已完成，直接更新 UI
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (entry.Direction == "RX")
            {
                AddRxEntry(entry, ...);
            }
            else
            {
                AddTxEntry(entry, ...);
            }
        });
    }
}
```

## 4. 实现计划

### 阶段 1：基础设施
1. 扩展 `ProtocolSchema` 添加 `AutoMatchConfig`
2. 实现 `ParserFingerprint` 模型
3. 实现 `AutoParserMatcher` 匹配器

### 阶段 2：帧缓冲区
1. 实现 `FrameBuffer` 字节级缓冲
2. 支持多种帧格式的自动切分
3. 集成到 `DataFlowViewModel`

### 阶段 3：智能匹配
1. 在 `ParserManager` 中集成指纹缓存
2. 实现自动匹配逻辑
3. 支持多解析器并存

### 阶段 4：UI 增强
1. 实时字段展示优化
2. 匹配状态指示器
3. 解析器切换建议

## 5. 兼容性考虑

- 保持现有手动选择解析器的功能
- `AutoMatch.Enabled = false` 时回退到手动模式
- 帧缓冲区大小可配置
- 匹配超时机制防止死锁
