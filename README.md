# ACCCOM 串口调试工具

Windows 桌面串口调试工具，对标 SSCOM 5.13.1。支持自定义协议解析脚本（C# Script）、HTTP API、MCP Server（AI 可直接调用），实现从"看原始 Hex"到"看懂数据含义"的完整闭环。

## 系统要求

| 项目 | 要求 |
|------|------|
| 操作系统 | Windows 10 1607+ / Windows Server 2019+ |
| 运行时 | .NET 8 运行时（[下载](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)） |
| 编译环境 | .NET 8 SDK |
| 架构 | x64 |

## 快速开始

```bash
# 编译全部项目
dotnet build ACCcom.sln

# 运行 WPF 桌面客户端
dotnet run --project src\ACCcom\ACCcom.csproj

# 运行 MCP Server（stdio 传输，供 AI 客户端调用）
dotnet run --project src\ACCcom.McpServer\ACCcom.McpServer.csproj

# 发布 WPF 单文件
dotnet publish src\ACCcom\ACCcom.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist\

# 发布 MCP Server
dotnet publish src\ACCcom.McpServer\ACCcom.McpServer.csproj -c Release -r win-x64 --self-contained true -o dist-mcp\
```

## 功能

### 串口配置
- 端口、波特率（300~921600）、数据位、停止位、校验位
- DTR/RTS 流控
- 自动扫描可用串口

### 数据收发
- 双窗口独立显示：接收区 (RX) / 发送区 (TX)，带时间戳、方向、原始 HEX、文本
- HEX / ASCII 发送切换
- RX/TX 数据统计（帧数 + 字节数）
- 实时连接时长显示 (hh:mm:ss)
- 发送数据自动回显到 TX 区
- 快捷发送栏（预设 AT 指令，支持自定义添加/删除，持久化到 `shortcuts.json`）
- 循环发送（可设间隔，毫秒级）
- 发送历史（上下键翻阅）
- RX/TX 面板实时关键字过滤（忽略大小写）
- 断线自动重连（最多 10 次，1 秒间隔）
- 深色/浅色主题切换

### 数据缓冲区

高性能数据缓冲系统，采用 `Channel<T>` + `RingBuffer` 双层架构：

- `Channel<LogEntry>`：无锁异步管道，支持多读多写，用于事件分发
- `RingBuffer`（默认容量 10000 条）：固定大小环形缓冲，O(1) 写入，快照读取
- 支持 `WaitForEntry` 阻塞等待匹配数据（用于 `wait-for` API / MCP `wait_for_response`）
- 支持增量拉取（`sinceId`）、方向过滤（RX/TX）、关键字过滤

### 多端口并发

`MultiPortService` 支持同时打开多个串口，每个端口独立管理生命周期：

- 按标签（tag）标识每个端口实例
- 统一事件总线：所有端口的数据通过 `OnDataReceived` 事件汇总，自动附加 `PortTag` 标识
- 独立的连接/断开/错误事件
- MCP 工具：`open_port_tagged`、`close_port_tagged`、`send_to_port_tagged`

### 网络桥接

`NetworkBridgeService` 支持 TCP/UDP 网络连接，将网络数据桥接到串口数据流中：

- TCP 客户端：`ConnectTcp(host, port)` — 建立 TCP 连接，自动接收循环
- UDP 客户端：`ConnectUdp(host, port)` — 建立 UDP 连接，支持异步接收
- 零拷贝接收：使用 `ArrayPool<byte>` + buffer+offset+count 直接传递，消除每包 byte[] 分配
- 网络数据统一转为 `LogEntry` 事件，与串口数据共用同一缓冲区和解析管道

### 自动波特率检测

`AutoBaudDetector` 自动探测设备波特率：

- 按优先级依次尝试：9600 → 115200 → 57600 → 38400 → 19200 → 4800 → 2400 → 1200 → 460800 → 230400 → 921600
- 发送探测字节（0x00 × 3），检测是否有响应
- MCP 工具：`detect_baud_rate`

### 协议测试运行器

`ProtocolTestRunner` 执行结构化的协议测试脚本（`TestScript` 模型）：

- 定义测试步骤：每步包含命令、超时、预期响应模式
- 自动匹配验证：contains / regex / exact
- 输出测试报告：逐步 pass/fail 结果、执行时间、错误详情
- 支持 `send_batch` MCP 工具批量发送命令序列

### 会话录制

`SessionRecorder` 将串口通信完整录制到 JSONL 文件：

- 格式：每行一个 JSON 对象，包含 `id`、`timestamp`、`direction`、`rawHex`、`text`、`fields`
- 异步写入 + 定时刷新（Timer），避免频繁 IO
- 自动创建 `recordings/` 目录
- MCP 工具：`start_recording`、`stop_recording`、`replay_session`

### 书签管理

`BookmarkManager` 在收发数据中标记关键条目：

- 按条目 ID 添加书签，自动记录方向、时间戳、预览文本
- 支持上下导航跳转到对应条目
- 与 UI 书签列表双向绑定（`ObservableCollection<BookmarkItem>`）

### 预设管理

`PresetManager` 管理串口配置预设，持久化到 `presets.json`：

- 保存/加载串口配置组合（端口、波特率、数据位、停止位、校验位、DTR/RTS）
- 快速切换不同设备的配置方案

### 快捷键管理

`ShortcutManager` 管理快捷发送栏项，持久化到 `shortcuts.json`：

- 支持自定义添加/删除快捷发送指令
- 默认预置常用 AT 指令（AT、AT+GMR、AT+RST 等）
- JSON 序列化持久化

### 设置管理

`SettingsService` 管理应用全局设置，持久化到 `settings.json`：

- 加载/保存 `AppSettings` 配置对象
- 文件损坏时自动回退到默认设置
- 覆盖主题、语言、缓冲区大小等全局选项

### 数据导出

`FileExportService` 支持多种格式导出收发数据：

- **TXT**：`[时间戳][方向] RawHex | Text` 纯文本格式
- **JSON**：结构化 JSON 数组，包含 timestamp、direction、hex、text、fields
- **CSV**：逗号分隔格式，适合 Excel 打开
- 支持 RX/TX 分别导出

### 数据统计

`DataStatistics` 实时统计串口数据性能指标：

- `RxBytesPerSecond`：接收速率（字节/秒）
- `RxFramesPerSecond`：接收帧率（帧/秒）
- `ErrorRate`：错误率（%）
- `AvgFrameIntervalMs`：平均帧间隔（毫秒）
- 基于 `ConcurrentQueue` 的滑动窗口计算（保留最近 10 秒样本）
- MCP 工具：`get_statistics`

### 触发器系统

`TriggerService` 基于规则的数据触发引擎：

- 每条规则（`TriggerRule`）定义：名称、方向过滤、匹配模式（contains / regex）、是否匹配 HEX、是否启用
- 数据到达时逐条评估匹配规则，触发 `OnTriggerFired` 事件
- 可联动执行自动化动作（如自动回复、日志记录）

### 宏引擎

`MacroManager` 管理多步骤宏模板（`MacroTemplate`）：

- 每个宏包含多个步骤：命令、是否 HEX、步骤间延时
- 支持模板变量替换
- 异步执行，支持取消
- 宏模板持久化到 `macros.json`

### 实时绘图

`PlotWindow` 实时绘制串口数据波形：

- 基于 `PlotViewModel` 实时更新
- 支持自定义字段偏移量、数据类型、缩放
- 适合连续数据流的可视化分析（如传感器读数、电流曲线）

### 统计仪表盘

`StatsWindow` 串口数据统计可视化：

- 基于 `StatsViewModel` 展示实时统计指标
- 接收速率、帧率、错误率等指标的图形化展示

### 数据对比

`CompareWindow` 逐帧对比两组数据：

- 选择两个条目进行字段级对比
- 高亮差异字段
- MCP 工具：`compare_frames`

### 数据差异

`DiffWindow` 数据差异分析：

- 两组数据之间的差异可视化
- 支持并排查看和差异高亮

### 会话回放

`ReplayWindow` 回放历史录制的会话文件：

- 加载 JSONL 格式的录制文件
- 逐条回放并还原解析结果
- MCP 工具：`replay_session`

### 文件操作
- RX/TX 数据一键保存 `.txt`
- 收发日志自动写入 `logs/` 目录，自动轮转（单文件 5MB）

### 协议解析引擎（脚本驱动）

基于 Roslyn C# Script，用户根据协议文档编写 `.csx` 解析脚本，串口数据到达后自动解析为结构化字段。

**脚本模板（`parsers/` 目录）：**
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

内置辅助函数：`RawHex`, `ToUInt16`, `ToInt16`, `ToFloat`, `Crc16`, `Sum8`, `Xor8`（定义在 `ScriptGlobals.cs`）

- 脚本热加载：修改 `.csx` 文件后即时生效
- 离线解析：不连串口也可直接解析 Hex 数据
- UI 中选中条目即可查看字段树

### HTTP API（内嵌 EmbedIO，端口 8899）

所有接口统一返回 JSON 格式：`{ "success": bool, "error": string?, "data": object? }`

#### 串口管理

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/ports` | 列出可用串口 |
| GET | `/api/status` | 获取连接状态、当前配置、收发计数 |
| POST | `/api/port/open` | 打开串口 |
| POST | `/api/port/close` | 关闭串口 |
| GET | `/api/health` | 健康检查 |

**打开串口：**

```bash
curl -X POST -H "Content-Type: application/json" \
  -d '{"Port":"COM3","BaudRate":115200,"DataBits":8,"StopBits":1,"Parity":0,"Dtr":false,"Rts":false}' \
  http://127.0.0.1:8899/api/port/open
```

> 注意：JSON 属性名采用 PascalCase（首字母大写），与 C# 模型属性名一致。

**获取状态：**

```bash
curl http://127.0.0.1:8899/api/status
# → {"success":true,"data":{"isOpen":true,"currentPort":"COM3","baudRate":115200,"rxCount":42,"txCount":10,"bufferCount":52}}
```

#### 数据收发

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/send` | 发送数据到串口 |
| GET | `/api/data` | 获取收发数据（支持增量拉取、过滤、限制） |
| POST | `/api/wait-for` | 阻塞等待匹配数据（超时返回） |
| POST | `/api/clear` | 清空数据缓冲区 |

**发送 ASCII 数据（JSON）：**

```bash
curl -X POST -H "Content-Type: application/json" \
  -d '{"Data":"AT+GMR","IsHex":false}' \
  http://127.0.0.1:8899/api/send
```

**发送 HEX 数据：**

```bash
curl -X POST -H "Content-Type: application/json" \
  -d '{"Data":"41 54 2B 47 4D 52 0D 0A","IsHex":true}' \
  http://127.0.0.1:8899/api/send
```

**纯文本发送（向后兼容）：**

```bash
# ASCII
curl -X POST -d "AT+GMR" http://127.0.0.1:8899/api/send

# HEX（hex: 前缀）
curl -X POST -d "hex:41542B474D520D0A" http://127.0.0.1:8899/api/send
```

**获取数据（增量拉取 + 过滤）：**

```bash
# 获取 since=0 之后的所有数据，最多 100 条
curl "http://127.0.0.1:8899/api/data?since=0&limit=100"

# 只获取 RX 方向的数据
curl "http://127.0.0.1:8899/api/data?since=0&direction=RX"
```

**等待设备响应（关键功能）：**

```bash
# 等待包含 "OK" 的响应，超时 5 秒
curl -X POST -H "Content-Type: application/json" \
  -d '{"Pattern":"OK","TimeoutMs":5000,"MatchMode":"contains","Direction":"RX"}' \
  http://127.0.0.1:8899/api/wait-for

# 正则匹配
curl -X POST -H "Content-Type: application/json" \
  -d '{"Pattern":"AT\\+\\w+","TimeoutMs":3000,"MatchMode":"regex"}' \
  http://127.0.0.1:8899/api/wait-for
```

`wait-for` 参数说明：
- `pattern`：匹配内容（必填）
- `timeoutMs`：超时毫秒数，默认 5000，范围 100~60000
- `matchMode`：`contains`（默认）、`regex`、`exact`
- `matchHex`：是否在 HEX 数据上匹配（默认 false，即在文本上匹配）
- `direction`：仅匹配指定方向 `RX`/`TX`（默认任意）

**清空缓冲区：**

```bash
curl -X POST -H "Content-Type: application/json" \
  -d '{"Target":"rx"}' http://127.0.0.1:8899/api/clear
# Target: "rx" | "tx" | "all"（默认 all）
```

#### 协议解析器

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/parsers` | 列出可用解析器及当前激活项 |
| GET | `/api/parser/read?name=xxx` | 读取解析器 .csx 源码 |
| POST | `/api/parser/write` | 写入/更新 .csx 解析器脚本 |
| POST | `/api/parser/activate` | 激活/停用指定解析器 |
| POST | `/api/parser/parse-raw` | 离线解析 Hex 数据（无需串口） |

```bash
# 列出解析器
curl http://127.0.0.1:8899/api/parsers

# 读取解析器源码
curl "http://127.0.0.1:8899/api/parser/read?name=sample"

# 写入/更新解析器脚本
curl -X POST -H "Content-Type: application/json" \
  -d '{"Name":"my-protocol","Code":"var result = new List<FieldAnnotation>();\nreturn result;"}' \
  http://127.0.0.1:8899/api/parser/write

# 激活解析器
curl -X POST -H "Content-Type: application/json" \
  -d '{"Name":"my-protocol"}' http://127.0.0.1:8899/api/parser/activate

# 停用
curl -X POST -H "Content-Type: application/json" \
  -d '{"Name":null}' http://127.0.0.1:8899/api/parser/activate

# 离线解析 Hex 数据
curl -X POST -H "Content-Type: application/json" \
  -d '{"Hex":"AA 55 06 01 19 2E"}' http://127.0.0.1:8899/api/parser/parse-raw

# 指定解析器离线解析
curl -X POST -H "Content-Type: application/json" \
  -d '{"Hex":"AA 55 06 01 19 2E","ParserName":"sample"}' \
  http://127.0.0.1:8899/api/parser/parse-raw
```

### i18n 国际化

基于 `LanguageManager` 的多语言支持：

- 内置语言包：中文（`zh-CN.json`）、英文（`en-US.json`）
- 运行时切换，无需重启
- 所有 UI 文本通过资源键引用

### 深色/浅色主题

基于 WPF ResourceDictionary 的主题切换：

- `DarkTheme.xaml`：深色主题
- `LightTheme.xaml`：浅色主题
- 运行时无缝切换

### WebSocket 实时推送

`HttpService` 内嵌 `SerialWebSocketHandler`，提供 WebSocket 端点 `/ws`：

- 数据到达时自动推送到所有已连接的 WebSocket 客户端
- 格式与 HTTP API `read_data` 一致
- 适合实时监控面板、Web 端数据流可视化

### 串口连接管理器

`SerialConnectionManager` 管理串口连接生命周期：

- 连接/断开切换（`ToggleConnection`）
- 连接时长实时追踪（每秒触发 `DurationChanged` 事件）
- 格式化时长显示（`hh:mm:ss`）

### 快捷键

| 按键 | 行为 |
|------|------|
| Enter | 发送输入框内容 |
| Ctrl+Enter | 插入换行 |
| Ctrl+S | 保存接收区数据 |
| ESC | 清空 RX/TX |

## 架构

### ViewModel 分层

MainViewModel 从 1274 行上帝类拆分为 4 个职责清晰的 ViewModel：

| ViewModel | 职责 | 行数 |
|-----------|------|------|
| `MainViewModel` | 协调者：持有子 VM 引用，管理设置和统计定时器 | 328 |
| `ConnectionViewModel` | 串口/网络连接管理、配置、重连 | 164 |
| `DataFlowViewModel` | 数据收发、过滤、解析、导出 | 371 |
| `ToolViewModel` | 快捷发送、预设、宏、书签、触发器、多端口 | 500 |

### 基类体系

- `ObservableObject` — MVVM 基类，统一 `INotifyPropertyChanged` + `SetField<T>` 实现
- `JsonFilePersistenceManager<T>` — JSON 持久化基类，ShortcutManager/PresetManager/MacroManager 继承
- `BufferedFileWriter` — 缓冲写入基类，LoggerService/SessionRecorder 继承（定时刷新 + 计数器）

### 性能优化

- **数据缓冲**：`Channel<T>` + `RingBuffer(10000)` + `ReaderWriterLockSlim`，零锁竞争读写
- **UI 渲染**：`ObservableRangeCollection` 批量移除触发单次 Reset 事件，ListBox 启用虚拟化 + Recycling
- **热路径**：Span 零分配 Hex 转换（`HexHelper`），预编译正则，`foreach` 替代 LINQ `.Any()`
- **内存池**：`ArrayPool<byte>` 复用收发缓冲区，`Pool<byte>` 替代 `new byte[]`
- **I/O 缓冲**：LoggerService/SessionRecorder 2s 定时器 + 100 写计数器批量刷盘
- **脚本引擎**：LRU 编译缓存（最多 10 个），同步快速路径避免 `Task.Run` 线程池开销

## 技术栈

| 层级 | 技术 |
|------|------|
| 桌面框架 | WPF (.NET 8) |
| 串口通信 | System.IO.Ports |
| HTTP 服务 | EmbedIO |
| WebSocket | EmbedIO WebSocketModule |
| AI 集成 | ModelContextProtocol SDK |
| 脚本引擎 | Roslyn C# Script + LRU 编译缓存 |
| 缓冲区 | System.Threading.Channels + RingBuffer |
| 架构 | MVVM (ObservableObject 基类) |
| 测试 | xUnit 2.5.3 (168 个测试) |

## 项目结构

```
ACCcom/
├── ACCcom.sln
├── launch_acccom.ps1               # 启动 MCP Server（直接模式，无需桌面端）
├── launch_acccom_gui.ps1           # 单独启动 WPF 桌面端（用于可视化监控）
├── src/
│   ├── ACCcom.Core/                # 共享核心库（net8.0，无 WPF 依赖）
│   │   ├── Models/
│   │   │   ├── ApiResponse.cs       # 统一 API 响应格式
│   │   │   ├── ApiRequests.cs       # 请求体模型
│   │   │   ├── AppSettings.cs       # 应用设置模型
│   │   │   ├── BookmarkItem.cs      # 书签条目模型
│   │   │   ├── FieldAnnotation.cs   # 协议字段标注
│   │   │   ├── LogEntry.cs          # 收发日志条目
│   │   │   ├── MacroTemplate.cs     # 宏模板模型
│   │   │   ├── SerialConfig.cs      # 串口配置模型
│   │   │   ├── SerialPreset.cs      # 串口预设模型
│   │   │   ├── ShortcutItem.cs      # 快捷发送项模型
│   │   │   ├── TestScript.cs        # 测试脚本模型
│   │   │   └── TriggerRule.cs       # 触发器规则模型
│   │   ├── Services/
│   │   │   ├── SerialService.cs     # 串口管理
│   │   │   ├── SerialConnectionManager.cs  # 连接生命周期管理
│   │   │   ├── HttpService.cs       # HTTP REST API + WebSocket (EmbedIO)
│   │   │   ├── DataBufferService.cs # Channel<T> + RingBuffer 数据缓冲
│   │   │   ├── BufferedFileWriter.cs # 缓冲写入基类 (定时刷新 + 计数器)
│   │   │   ├── LoggerService.cs     # 文件日志 (继承 BufferedFileWriter)
│   │   │   ├── ParserEngine.cs      # Roslyn .csx 脚本引擎 + LRU 编译缓存
│   │   │   ├── ParserManager.cs     # 解析器热加载管理
│   │   │   ├── ScriptGlobals.cs     # .csx 脚本辅助方法
│   │   │   ├── AutoBaudDetector.cs  # 自动波特率检测
│   │   │   ├── MultiPortService.cs  # 多端口并发管理
│   │   │   ├── NetworkBridgeService.cs # TCP/UDP 网络桥接
│   │   │   ├── MacroManager.cs      # 宏模板管理 (继承 JsonFilePersistenceManager)
│   │   │   ├── ProtocolTestRunner.cs # 协议测试运行器
│   │   │   ├── SessionRecorder.cs   # 会话录制 (继承 BufferedFileWriter)
│   │   │   ├── BookmarkManager.cs   # 书签管理
│   │   │   ├── PresetManager.cs     # 预设管理 (继承 JsonFilePersistenceManager)
│   │   │   ├── ShortcutManager.cs   # 快捷键管理 (继承 JsonFilePersistenceManager)
│   │   │   ├── JsonFilePersistenceManager.cs # JSON 持久化基类
│   │   │   ├── SettingsService.cs   # 设置管理
│   │   │   ├── FileExportService.cs # 数据导出 (TXT/JSON/CSV)
│   │   │   ├── DataStatistics.cs    # 数据统计
│   │   │   ├── TriggerService.cs    # 触发器系统
│   │   │   └── HexHelper.cs         # HEX 工具方法
│   │   └── parsers/                 # 内置协议解析器
│   │       ├── dirui_protocol.csx   # 迪瑞生化分析仪协议
│   │       ├── esoac_v3.csx
│   │       ├── modbus_rtu_template.csx
│   │       ├── sample.csx
│   │       └── simple_frame_template.csx
│   ├── ACCcom/                     # WPF 桌面客户端
│   │   ├── App.xaml / MainWindow.xaml
│   │   ├── PlotWindow.xaml         # 实时绘图窗口
│   │   ├── StatsWindow.xaml        # 统计仪表盘窗口
│   │   ├── CompareWindow.xaml      # 数据对比窗口
│   │   ├── DiffWindow.xaml         # 数据差异窗口
│   │   ├── ReplayWindow.xaml       # 会话回放窗口
│   │   ├── AddShortcutDialog.xaml  # 快捷键添加对话框
│   │   ├── LanguageManager.cs      # 国际化管理器
│   │   ├── Languages/              # 语言包
│   │   │   ├── zh-CN.json
│   │   │   └── en-US.json
│   │   ├── Themes/                 # 主题资源
│   │   │   ├── DarkTheme.xaml
│   │   │   └── LightTheme.xaml
│   │   ├── Converters/             # WPF 值转换器
│   │   ├── ViewModels/
│   │   │   ├── ObservableObject.cs       # MVVM 基类 (INotifyPropertyChanged + SetField)
│   │   │   ├── MainViewModel.cs          # 协调者 (328行，持有子VM引用)
│   │   │   ├── ConnectionViewModel.cs    # 串口/网络连接管理
│   │   │   ├── DataFlowViewModel.cs      # 数据收发、过滤、解析
│   │   │   ├── ToolViewModel.cs          # 快捷发送、预设、宏、书签、触发器
│   │   │   ├── PlotViewModel.cs
│   │   │   ├── StatsViewModel.cs
│   │   │   ├── PortItemViewModel.cs
│   │   │   ├── ObservableRangeCollection.cs
│   │   │   └── RelayCommand.cs
│   │   └── parsers/               # WPF 端解析器（运行时复制）
│   └── ACCcom.McpServer/          # MCP Server (stdio 传输)
│       ├── Program.cs
│       └── Tools/
│           ├── SerialTools.cs      # 串口操作工具 (16 个)
│           ├── ParserTools.cs      # 解析器工具 (5 个)
│           ├── AnalysisTools.cs    # 分析工具 (2 个)
│           ├── RecordingTools.cs   # 录制回放工具 (3 个)
│           └── ToolContext.cs      # 工具上下文
├── tests/
│   └── ACCcom.Core.Tests/          # 单元测试 (168 个)
│       ├── DataBufferServiceTests.cs
│       ├── DataBufferServiceConcurrencyTests.cs
│       ├── TriggerServiceTests.cs
│       ├── MacroManagerRunAsyncTests.cs
│       ├── ParserEngineTests.cs
│       ├── ParserManagerTests.cs
│       ├── ScriptGlobalsTests.cs
│       ├── FileExportServiceTests.cs
│       ├── DataStatisticsTests.cs
│       ├── ProtocolTestRunnerTests.cs
│       ├── BookmarkManagerTests.cs
│       ├── ShortcutManagerTests.cs
│       ├── PresetManagerTests.cs
│       └── ... (共 14 个测试文件)
├── docs/
│   ├── design/
│   └── plan/
└── README.md
```

## AI 集成

### 方案一：MCP Server（推荐）

ACCcom.McpServer 是一个独立进程的 MCP stdio 服务器，AI 客户端可直接启动并调用 26 个工具，无需 HTTP 配置。

**默认运行模式：**

| 模式 | 命令 | 说明 |
|------|------|------|
| 直连（默认） | `.\launch_acccom.ps1` 或 `dotnet run --project src/ACCcom.McpServer/ACCcom.McpServer.csproj` | 独立管理串口，无需桌面端，MCP 工具直接可用 |
| 代理（需桌面端） | `.\launch_acccom_gui.ps1` | 单独启动 WPF 桌面端用于可视化监控 |

> 在 OpenCode 中，MCP Server 配置使用 `launch_acccom.ps1`，默认以**直接模式**运行，串口工具始终可用且不会弹出 GUI。需要桌面端时，在 opencode 中运行 `/acccom-gui` 或直接执行 `.\launch_acccom_gui.ps1`。

**AI 自动化串口调试完整工作流：**

```
阅读设备协议文档
  → write_parser("my-device", .csx脚本代码)     # AI 生成解析脚本
  → activate_parser("my-device")                 # 激活
  → open_port(port="COM3", baudRate=115200)     # 打开串口
  → send(data="AT+GMR")                         # 发送指令
  → send_and_wait(data="AT+GMR", pattern="OK", timeoutMs=3000)  # 发送并等待响应
  → read_data(sinceId=0, direction="RX")        # 读取响应
  → parse_raw(hex="AA 55 03 01 19 2E")          # 离线调试解析结果
```

**可用 MCP Tools（26 个）：**

| Tool | 说明 |
|------|------|
| `list_ports` | 列出可用串口 |
| `get_status` | 连接状态、配置、收发计数 |
| `health_check` | MCP 服务器健康检查（运行时间、内存、解析器状态） |
| `open_port` | 打开串口（波特率、数据位、停止位、校验位、DTR/RTS） |
| `close_port` | 关闭串口 |
| `send` | 发送数据（ASCII 或 HEX） |
| `read_data` | 增量读取缓冲数据（支持 sinceId / limit / direction 过滤） |
| `wait_for_response` | 阻塞等待匹配数据（支持 contains / regex / exact 匹配，可超时） |
| `send_and_wait` | 发送数据并等待匹配响应（组合 send + wait_for_response，减少 AI 调用轮次） |
| `send_batch` | 批量发送多个命令并收集响应 |
| `clear_buffer` | 清空缓冲区（rx/tx/all） |
| `get_statistics` | 获取接收速率、错误率、帧间隔等统计信息 |
| `detect_baud_rate` | 自动探测设备波特率 |
| `open_port_tagged` | 打开额外串口（多端口并发，按标签标识） |
| `close_port_tagged` | 关闭指定标签的串口 |
| `send_to_port_tagged` | 向指定标签的串口发送数据 |
| `list_parsers` | 列出可用协议解析器 |
| `read_parser` | 读取解析器源码 |
| `write_parser` | 写入/更新 .csx 解析器脚本（AI 生成脚本的关键入口） |
| `activate_parser` | 激活/停用解析器 |
| `parse_raw` | 离线解析 Hex 数据（无需串口，用于验证解析器是否正确） |
| `analyze_protocol` | 批量分析协议数据（字段统计、错误分布、解析结果） |
| `compare_frames` | 逐帧对比两组 Hex 数据的字段差异 |
| `start_recording` | 开始录制串口通信到 JSONL 文件 |
| `stop_recording` | 停止当前录制 |
| `replay_session` | 回放历史录制的会话文件 |

### 方案二：HTTP API（备用）

ACCCOM WPF 桌面端启动时自动监听 `http://127.0.0.1:8899`，通过 REST API 控制串口。

```powershell
# 发送指令并等待响应
Invoke-RestMethod -Uri "http://127.0.0.1:8899/api/send" -Method Post -ContentType "application/json" -Body '{"data":"AT+GMR"}'
Invoke-RestMethod -Uri "http://127.0.0.1:8899/api/wait-for" -Method Post -ContentType "application/json" -Body '{"pattern":"OK","timeoutMs":3000}'

# 增量读取接收数据
$data = Invoke-RestMethod -Uri "http://127.0.0.1:8899/api/data?since=0"
$data.data.entries | ForEach-Object { "[$($_.timestamp)][$($_.direction)] $($_.text)" }
```

### 数据格式

所有输出统一格式（日志 / API / MCP）：

```
[14:30:22.123][RX] 41 54 2B 47 4D 52 0D 0A | AT+GMR
```

- `RawHex`：十六进制，空格分隔
- `Text`：UTF-8 解码文本
- `Fields`：激活解析器后，RX 条目自动附带结构化字段标注

## 许可

MIT
