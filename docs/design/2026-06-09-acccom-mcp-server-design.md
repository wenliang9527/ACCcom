## ACCCOM MCP 服务器设计文档

### 1. 目标

将 ACCCOM 串口调试工具改造为标准 MCP（Model Context Protocol）服务器，让 AI 客户端（Claude Desktop、Cursor、QoderWork 等）能够：

1. **自主控制串口**：列出端口、打开/关闭、发送数据、读取响应
2. **智能等待响应**：发送指令后阻塞等待设备回复，而非轮询
3. **管理协议解析器**：AI 阅读协议文档 → 生成 .csx 脚本 → 写入 ACCCOM → 自动解析
4. **离线解析**：不经过串口，直接给 Hex 数据让解析器解析

### 2. 架构设计

#### 2.1 项目拆分

当前所有代码在 WPF 项目中。MCP 服务器需要以 stdio 方式运行（无窗口），因此需要拆分：

```
ACCcom.sln
├── src/
│   ├── ACCcom.Core/              ← 新建：共享核心库（net8.0，无平台依赖）
│   │   ├── ACCcom.Core.csproj
│   │   ├── Models/
│   │   │   ├── SerialConfig.cs
│   │   │   ├── LogEntry.cs
│   │   │   ├── FieldAnnotation.cs
│   │   │   ├── ApiResponse.cs
│   │   │   └── ApiRequests.cs
│   │   └── Services/
│   │       ├── SerialService.cs
│   │       ├── LoggerService.cs
│   │       ├── ParserEngine.cs
│   │       ├── ParserManager.cs
│   │       ├── ScriptGlobals.cs
│   │       └── HttpService.cs      ← 可选保留在 WPF 中
│   │
│   ├── ACCcom.Wpf/               ← 原 ACCcom 项目重命名
│   │   ├── ACCcom.Wpf.csproj         （引用 ACCcom.Core）
│   │   ├── App.xaml / MainWindow.xaml
│   │   ├── ViewModels/
│   │   ├── Converters/
│   │   └── Services/
│   │       └── HttpService.cs      ← HTTP API 留在 WPF 层（GUI 调试用）
│   │
│   └── ACCcom.McpServer/         ← 新建：MCP 服务器（控制台应用）
│       ├── ACCcom.McpServer.csproj    （引用 ACCcom.Core + MCP SDK）
│       └── Program.cs               （入口：stdio 传输 + 工具注册）
```

#### 2.2 拆分原则

- **ACCcom.Core** 只包含纯业务逻辑，不引用 WPF、EmbedIO 或任何 UI 框架。ParserManager 中的 `SynchronizationContext` 调度改为条件编译或回调注入
- **ACCcom.Wpf** 引用 ACCcom.Core，只保留 UI 相关代码（ViewModel、Converter、XAML）和 HTTP API
- **ACCcom.McpServer** 引用 ACCcom.Core + `ModelContextProtocol` NuGet 包，是一个轻量控制台程序

#### 2.3 ParserManager 的 UI 解耦

当前 `ParserManager` 构造函数捕获 `SynchronizationContext.Current` 用于将文件变更通知调度到 UI 线程。拆分后 Core 层没有 UI 上下文。

解决方案：将调度逻辑改为可选的回调注入：

```csharp
public class ParserManager : IDisposable
{
    private readonly Action<Action>? _dispatch;  // null = 直接执行（MCP/无 UI 场景）

    public ParserManager(Action<Action>? dispatch = null)
    {
        _dispatch = dispatch;
        // ...
    }

    private void Dispatch(Action action)
    {
        if (_dispatch != null) _dispatch(action);
        else action();  // 无 UI 时直接同步执行
    }
}
```

WPF 层传入 `action => Application.Current.Dispatcher.Invoke(action)`，MCP 层传 `null`。

### 3. MCP 协议适配

#### 3.1 SDK 与传输

使用官方 C# SDK：`ModelContextProtocol`（NuGet）。

传输方式选择 **stdio**：AI 客户端通过标准输入/输出与 MCP 服务器通信，这是 Claude Desktop、Cursor 等客户端的首选模式。

未来可选扩展 Streamable HTTP 端点，用于远程/云端调用。

#### 3.2 MCP 服务器启动方式

AI 客户端配置示例（Claude Desktop `claude_desktop_config.json`）：

```json
{
  "mcpServers": {
    "acccom": {
      "command": "ACCcom.McpServer.exe",
      "args": ["--parsers-dir", "C:\\Users\\xxx\\acccom-parsers"]
    }
  }
}
```

或通过 dotnet 运行：

```json
{
  "mcpServers": {
    "acccom": {
      "command": "dotnet",
      "args": ["run", "--project", "D:\\path\\to\\ACCcom\\src\\ACCcom.McpServer"]
    }
  }
}
```

#### 3.3 MCP 资源（Resources）

除工具外，还可暴露只读资源让 AI 浏览：

| Resource URI | 说明 |
|---|---|
| `acccom://parsers` | 列出所有解析器及其描述 |
| `acccom://parser/{name}` | 读取指定解析器的 .csx 源码 |
| `acccom://logs/latest` | 读取最新日志文件内容 |

### 4. 完整 Tool 清单

#### 4.1 串口管理

| Tool 名称 | 输入参数 | 说明 |
|---|---|---|
| `list_ports` | 无 | 列出系统可用串口 |
| `get_status` | 无 | 连接状态、当前端口、波特率、RX/TX 计数 |
| `open_port` | `port`, `baudRate?`, `dataBits?`, `stopBits?`, `parity?`, `dtr?`, `rts?` | 打开指定串口 |
| `close_port` | 无 | 关闭当前串口 |

#### 4.2 数据收发

| Tool 名称 | 输入参数 | 说明 |
|---|---|---|
| `send` | `data`, `isHex?` | 发送数据到串口 |
| `read_data` | `sinceId?`, `limit?`, `direction?` | 获取收发数据（增量拉取） |
| `wait_for_response` | `pattern`, `timeoutMs?`, `matchMode?`, `matchHex?`, `direction?` | 阻塞等待匹配数据 |
| `clear_buffer` | `target?` (rx/tx/all) | 清空数据缓冲区 |

#### 4.3 协议解析器管理

| Tool 名称 | 输入参数 | 说明 |
|---|---|---|
| `list_parsers` | 无 | 列出可用解析器及当前激活项 |
| `read_parser` | `name` | 读取指定解析器的 .csx 源码 |
| `write_parser` | `name`, `code` | 写入/更新 .csx 解析脚本 |
| `activate_parser` | `name` (null=停用) | 激活/停用指定解析器 |
| `parse_raw` | `hex`, `parserName?` | 离线解析：Hex → 结构化字段 |

#### 4.4 各 Tool 的 MCP 注册示例

```csharp
// Program.cs 片段
server.AddTool("open_port",
    "打开指定串口。参数: port(必填), baudRate(默认115200), dataBits(默认8), stopBits(0=None/1=One/2=Two), parity(0=None/1=Odd/2=Even), dtr, rts",
    new { port = typeof(string), baudRate = typeof(int?), dataBits = typeof(int?),
          stopBits = typeof(int?), parity = typeof(int?), dtr = typeof(bool?), rts = typeof(bool?) },
    async (args) =>
    {
        var config = new SerialConfig { PortName = (string)args["port"], ... };
        var ok = serial.Open(config);
        return ok
            ? new { success = true, data = new { port = config.PortName, baudRate = config.BaudRate } }
            : new { success = false, error = $"打开串口 {config.PortName} 失败" };
    });
```

### 5. 协议解析闭环工作流

这是 ACCCOM MCP 最有价值的部分——让 AI 成为协议解析器的作者和维护者。

#### 5.1 闭环流程

```
用户有设备协议文档（PDF/手册/口头描述）
         │
         ▼
    AI 阅读协议文档
         │
         ▼
    AI 生成 .csx 解析脚本
    (使用 ScriptGlobals 提供的 RawHex/ToUInt16/Crc16 等辅助方法)
         │
         ▼
    MCP: write_parser(name, code)
         │  写入 parsers/{name}.csx
         │
         ▼
    MCP: parse_raw(hex, parser)  ← 用已知样本验证
         │  返回结构化字段列表
         │
    AI 检查结果是否正确 ──→ 不对 → 修改 code → 重新 write_parser
         │
         ▼  正确
    MCP: activate_parser(name)  ← 激活实时解析
         │
         ▼
    MCP: open_port → 串口数据自动解析
         │
         ▼
    MCP: read_data → 获取带 Fields 的结构化数据
         │
    AI 分析业务含义，辅助调试决策
```

#### 5.2 .csx 脚本规范（AI 生成标准）

AI 生成的 .csx 脚本必须遵循以下规范：

```csharp
// 脚本输入（由引擎注入）:
//   RawData    : byte[]    — 原始字节数据
//   Timestamp  : DateTime  — 接收时间
//
// 可用辅助方法（ScriptGlobals）:
//   RawHex(offset, length)            → "AA 55 03"
//   ToUInt16(offset, bigEndian?)      → ushort
//   ToInt16(offset, bigEndian?)       → short
//   ToFloat(offset, bigEndian?)       → float
//   Crc16(offset, length)             → ushort (Modbus CRC16)
//   Sum8(offset, length)              → byte
//   Xor8(offset, length)              → byte
//
// 必须返回: List<FieldAnnotation>
// 每个 FieldAnnotation:
//   Name         : string          字段名
//   Offset       : int             起始字节偏移
//   Length       : int             字节长度
//   RawHex       : string          原始 Hex 显示
//   DisplayValue : string          可读值（如 "25.3 °C"）
//   Color        : string?         可选颜色 #RRGGBB
//   Severity     : FieldSeverity   Normal / Warning / Error

var result = new List<FieldAnnotation>();
if (RawData.Length < 5) return result;

result.Add(new FieldAnnotation { Name = "帧头", Offset = 0, Length = 2, ... });
result.Add(new FieldAnnotation { Name = "温度", Offset = 4, Length = 1, ... });
// ...
return result;
```

#### 5.3 parse_raw 的价值

`parse_raw` 工具让 AI 在不连接真实设备的情况下也能：

- 验证自己写的解析脚本是否正确
- 分析用户贴过来的 Hex 抓包数据
- 迭代修改脚本直到解析结果正确

这大幅降低了协议解析器的开发门槛——AI 可以在"离线"模式下反复调试，不需要反复连串口。

### 6. 状态管理

MCP 服务器是长驻进程，需要在多次工具调用间保持状态：

| 状态 | 生命周期 | 管理方式 |
|---|---|---|
| 串口连接 | 进程运行期间 | SerialService 实例，open_port/close_port 切换 |
| 数据缓冲区 | 进程运行期间 | 内存 List\<LogEntry\>，支持 clear_buffer 清空 |
| 解析器 | 文件变更时热加载 | ParserManager + FileSystemWatcher |
| 等待队列 | 单次调用 | wait_for_response 的 TaskCompletionSource，超时自动清理 |

每次 AI 会话结束时（stdio 断开），MCP 服务器应自动关闭串口并释放资源。

### 7. 与现有 HTTP API 的关系

| 层 | 用途 | 保留/废弃 |
|---|---|---|
| HTTP API (EmbedIO :8899) | GUI 调试、curl/PowerShell 快速测试、向后兼容 | 保留在 ACCcom.Wpf |
| MCP stdio | AI 客户端调用 | 新增，在 ACCcom.McpServer |

两者共享同一套 Core 业务逻辑，只是传输层不同。HTTP API 上一轮已完成的统一响应格式、wait-for、parser 管理等端点可以直接复用。

### 8. 迁移路径（分阶段实施）

#### 第一阶段：项目拆分

1. 创建 `ACCcom.Core` 类库项目（net8.0，无 WPF）
2. 移动 Models/ 和 Services/ 到 Core（SerialService、ParserEngine、ParserManager、LoggerService、ScriptGlobals）
3. 解耦 ParserManager 的 SynchronizationContext
4. ACCcom.Wpf 引用 ACCcom.Core，删除已移出的文件
5. 编译验证：WPF 功能不变

#### 第二阶段：MCP 服务器骨架

1. 创建 `ACCcom.McpServer` 控制台项目
2. 添加 `ModelContextProtocol` NuGet 包
3. 实现 stdio 传输 + 最小工具集（list_ports、get_status、open_port、close_port、send、read_data）
4. 在 Claude Desktop 或 QoderWork 中注册并验证连接

#### 第三阶段：完整工具集

1. 添加 wait_for_response（阻塞等待）
2. 添加 clear_buffer
3. 添加 list_parsers、read_parser、write_parser、activate_parser
4. 添加 parse_raw（离线解析）
5. 添加 MCP Resources（parsers 列表、解析器源码）

#### 第四阶段：打磨

1. 工具描述优化（中英文、示例、错误提示）
2. 错误处理完善（结构化错误码、重试建议）
3. 打包发布（单文件 exe、dotnet tool）
4. 编写 AI 使用指南（作为 MCP Prompt 暴露）

### 9. 依赖清单

| 包 | 项目 | 用途 |
|---|---|---|
| `System.IO.Ports` | ACCcom.Core | 串口通信 |
| `Microsoft.CodeAnalysis.CSharp.Scripting` | ACCcom.Core | .csx 脚本引擎 |
| `ModelContextProtocol` | ACCcom.McpServer | MCP 协议实现 |
| `EmbedIO` | ACCcom.Wpf | HTTP API（仅 GUI 版） |
