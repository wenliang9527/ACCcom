# 架构指南

ACCCOM 采用**模块化 MVVM 架构**，核心库与 WPF 桌面端严格分离，服务层通过依赖注入组装，数据流基于 `Channel<T>` + `RingBuffer` 无锁管道。整体设计强调可测试性、可扩展性和高性能，支持从单串口调试到多端口并发、协议解析到 AI 集成的完整闭环。

## 架构

### ViewModel 分层

`MainViewModel` 采用职责清晰的模块化设计，每个子 ViewModel 管理独立功能域：

| ViewModel | 职责 |
|-----------|------|
| `MainViewModel` | 协调者：持有子 VM 引用，管理设置和统计定时器 |
| `ConnectionViewModel` | 串口/网络连接管理、配置、重连 |
| `DataFlowViewModel` | 数据收发、过滤、解析、导出 |
| `ToolViewModel` | 快捷发送、预设、宏、书签、触发器、多端口 |
| `LoopSendViewModel` | 循环发送功能 |
| `ModbusViewModel` | Modbus 主站操作（读写寄存器、轮询、事务日志） |
| `ModbusConnectionViewModel` | Modbus 连接管理（RTU/TCP 连接） |
| `MultiPortViewModel` | 多端口并发管理 |
| `BookmarkViewModel` | 书签管理 |
| `MacroViewModel` | 宏模板管理 |
| `PresetViewModel` | 串口预设管理 |
| `ReplayViewModel` | 会话回放 |
| `ShortcutViewModel` | 快捷发送栏管理 |
| `TriggerViewModel` | 触发器规则管理 |
| `PlotViewModel` | 实时绘图 |
| `StatsViewModel` | 统计仪表盘 |

### 基类体系

- `ObservableObject` — MVVM 基类，统一 `INotifyPropertyChanged` + `SetField<T>` 实现
- `JsonFilePersistenceManager<T>` — JSON 持久化基类，`ShortcutManager`/`PresetManager`/`MacroManager` 继承
- `BufferedFileWriter` — 缓冲写入基类，`LoggerService`/`SessionRecorder` 继承（定时刷新 + 计数器）
- `ISerialService` — 串口服务接口，支持真实串口和虚拟串口
- `IModbusTransport` — Modbus 传输层接口，支持 RTU 和 TCP

### 性能优化

- **数据缓冲**：`Channel<T>` + `RingBuffer(10000)` + `ReaderWriterLockSlim`，零锁竞争读写
- **UI 渲染**：`ObservableRangeCollection` 批量移除触发单次 Reset 事件，ListBox 启用虚拟化 + Recycling
- **热路径**：Span 零分配 Hex 转换（`HexHelper`），预编译正则，`foreach` 替代 LINQ `.Any()`
- **内存池**：`ArrayPool<byte>` 复用收发缓冲区，`Pool<byte>` 替代 `new byte[]`
- **I/O 缓冲**：`LoggerService`/`SessionRecorder` 2s 定时器 + 100 写计数器批量刷盘
- **脚本引擎**：LRU 编译缓存（可配，默认 10 个），启动预热，同步快速路径避免 `Task.Run` 线程池开销
- **Modbus TCP**：异步接收循环 + `TaskCompletionSource` 响应匹配，支持心跳保活
- **Modbus 连接管理**：`ConcurrentDictionary` 管理多连接，支持 RTU/TCP 混合使用
- **协议解析器生成**：从 `ProtocolSchema` 自动生成优化的 `.csx` 代码，避免运行时反射开销

## 技术栈

| 层级 | 技术 |
|------|------|
| 桌面框架 | WPF (.NET 8) |
| 串口通信 | System.IO.Ports |
| 网络通信 | System.Net.Sockets (TCP/UDP) |
| HTTP 服务 | EmbedIO |
| WebSocket | EmbedIO WebSocketModule |
| AI 集成 | ModelContextProtocol SDK |
| Modbus | 自研 Modbus RTU/TCP 主从站实现 |
| 脚本引擎 | Roslyn C# Script + LRU 编译缓存 |
| 缓冲区 | System.Threading.Channels + RingBuffer |
| 架构 | MVVM (ObservableObject 基类) |
| 测试 | xUnit 2.5.3（350 个测试） |

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
│   │   │   ├── ModbusFunctionCode.cs # Modbus 功能码枚举
│   │   │   ├── ModbusPriority.cs    # Modbus 优先级定义
│   │   │   ├── ModbusResponse.cs    # Modbus 响应模型
│   │   │   ├── ModbusTransaction.cs # Modbus 事务模型
│   │   │   ├── ProtocolSchema.cs    # 协议描述格式
│   │   │   ├── SerialConfig.cs      # 串口配置模型
│   │   │   ├── SerialPreset.cs      # 串口预设模型
│   │   │   ├── ShortcutItem.cs      # 快捷发送项模型
│   │   │   ├── TestScript.cs        # 测试脚本模型
│   │   │   └── TriggerRule.cs       # 触发器规则模型
│   │   ├── Services/
│   │   │   ├── FrameAssembler.cs    # 多帧拼接重组
│   │   │   ├── FrameAssemblerConfig.cs
│   │   │   ├── SerialService.cs     # 串口管理
│   │   │   ├── SerialConnectionManager.cs  # 连接生命周期管理
│   │   │   ├── SerialController.cs  # HTTP API 控制器
│   │   │   ├── SerialWebSocketHandler.cs # WebSocket 处理器
│   │   │   ├── HttpService.cs       # HTTP REST API + WebSocket (EmbedIO)
│   │   │   ├── DataBufferService.cs # Channel<T> + RingBuffer 数据缓冲
│   │   │   ├── BufferedFileWriter.cs # 缓冲写入基类 (定时刷新 + 计数器)
│   │   │   ├── LoggerService.cs     # 文件日志 (继承 BufferedFileWriter)
│   │   │   ├── ParserEngine.cs      # Roslyn .csx 脚本引擎 + LRU 编译缓存
│   │   │   ├── ParserGenerator.cs   # 协议解析器生成器 (从 Schema 生成 .csx)
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
│   │   │   ├── HexHelper.cs         # HEX 工具方法
│   │   │   ├── IModbusTransport.cs  # Modbus 传输层接口
│   │   │   ├── ISerialService.cs    # 串口服务接口
│   │   │   ├── ModbusService.cs     # Modbus 主站服务
│   │   │   ├── ModbusConnectionManager.cs # Modbus 连接管理器
│   │   │   ├── ModbusRtuTransport.cs # Modbus RTU 传输层
│   │   │   ├── ModbusTcpTransport.cs # Modbus TCP 传输层
│   │   │   ├── ModbusRtuSlaveTransport.cs # Modbus RTU 从站传输
│   │   │   ├── ModbusTcpSlaveTransport.cs # Modbus TCP 从站传输
│   │   │   ├── ModbusSlaveService.cs # Modbus 从站服务
│   │   │   ├── ModbusSlaveDevice.cs # Modbus 从站设备模拟
│   │   │   ├── ModbusPriorityQueue.cs # Modbus 优先级队列
│   │   │   ├── ModbusUtils.cs       # Modbus 工具方法
│   │   │   └── VirtualSerialService.cs # 虚拟串口服务
│   │   └── parsers/                 # 内置协议解析器
│   │       ├── dirui_protocol.csx   # 迪瑞生化分析仪协议
│   │       ├── esoac_v3.csx
│   │       ├── modbus_rtu_template.csx # Modbus RTU 模板
│   │       ├── modbus_tcp_template.csx # Modbus TCP 模板
│   │       ├── sample.csx
│   │       └── simple_frame_template.csx
│   ├── ACCcom/                     # WPF 桌面客户端
│   │   ├── App.xaml / MainWindow.xaml
│   │   ├── PlotWindow.xaml         # 实时绘图窗口
│   │   ├── StatsWindow.xaml        # 统计仪表盘窗口
│   │   ├── CompareWindow.xaml      # 数据对比窗口
│   │   ├── DiffWindow.xaml         # 数据差异窗口
│   │   ├── ReplayWindow.xaml       # 会话回放窗口
│   │   ├── ModbusWindow.xaml       # Modbus 操作窗口
│   │   ├── ModbusConnectionDialog.xaml # Modbus 连接对话框
│   │   ├── AddShortcutDialog.xaml  # 快捷键添加对话框
│   │   ├── LanguageManager.cs      # 国际化管理器
│   │   ├── Languages/              # 语言包
│   │   │   ├── zh-CN.json
│   │   │   └── en-US.json
│   │   ├── SchemaEditorWindow.xaml     # 协议可视化编辑器
│   │   ├── FrameAssemblerConfigWindow.xaml # 多帧拼接配置
│   │   ├── Themes/                 # 主题资源
│   │   │   ├── DarkTheme.xaml
│   │   │   └── LightTheme.xaml
│   │   ├── Converters/             # WPF 值转换器
│   │   │   ├── FieldValuesTemplateSelector.cs
│   │   ├── ViewModels/
│   │   │   ├── SchemaEditorViewModel.cs   # 协议可视化编辑器
│   │   │   ├── FieldItemViewModel.cs      # 编辑器字段条目
│   │   │   ├── ObservableObject.cs       # MVVM 基类 (INotifyPropertyChanged + SetField)
│   │   │   ├── MainViewModel.cs          # 协调者 (持有子VM引用)
│   │   │   ├── ConnectionViewModel.cs    # 串口/网络连接管理
│   │   │   ├── DataFlowViewModel.cs      # 数据收发、过滤、解析
│   │   │   ├── ToolViewModel.cs          # 快捷发送、预设、宏、书签、触发器
│   │   │   ├── PlotViewModel.cs
│   │   │   ├── StatsViewModel.cs
│   │   │   ├── PortItemViewModel.cs
│   │   │   ├── LoopSendViewModel.cs     # 循环发送
│   │   │   ├── ModbusViewModel.cs       # Modbus 操作
│   │   │   ├── ModbusConnectionViewModel.cs # Modbus 连接管理
│   │   │   ├── MultiPortViewModel.cs    # 多端口管理
│   │   │   ├── BookmarkViewModel.cs     # 书签管理
│   │   │   ├── MacroViewModel.cs        # 宏管理
│   │   │   ├── PresetViewModel.cs       # 预设管理
│   │   │   ├── ReplayViewModel.cs       # 会话回放
│   │   │   ├── ShortcutViewModel.cs     # 快捷键管理
│   │   │   ├── TriggerViewModel.cs      # 触发器管理
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
│           ├── ModbusTools.cs      # Modbus 工具 (7 个)
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

## 许可

MIT

## 相关文档

- [集成指南](../guide/integration.md)
