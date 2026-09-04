## 第 18 轮:MCP Server 裁剪为纯串口基础接口

### 目标

把 ACCcom.McpServer 从 39 个工具(5 个工具类)裁剪为 **8 个基础串口工具**(1 个工具类),删除解析器、录制、分析、Modbus、多端口、统计、状态查询及 proxy 代理模式,只保留直连模式的开关串口与收发数据能力。

### 保留的 8 个工具(SerialTools.cs)

| 工具 | 说明 |
|------|------|
| `list_ports` | 列出可用串口 |
| `open_port` | 打开串口(波特率/数据位/停止位/校验位/DTR/RTS) |
| `close_port` | 关闭串口 |
| `send` | 发送数据(ASCII 或 HEX) |
| `read_data` | 增量读取缓冲数据(sinceId / limit / direction) |
| `wait_for_response` | 阻塞等待匹配数据(contains / regex / exact) |
| `send_and_wait` | 发送并等待响应(组合,调试刚需) |
| `clear_buffer` | 清空缓冲区 |

**删除的 31 个工具**:`get_status`、`health_check`、`open_port_tagged`、`close_port_tagged`、`send_to_port_tagged`、`get_statistics`、`detect_baud_rate`、`send_batch`、解析器 8 个(`list_parsers`/`read_parser`/`write_parser`/`activate_parser`/`parse_raw`/`generate_parser`/`validate_schema`/`get_schema_template`)、录制 4 个、分析 2 个、Modbus 9 个。

### 文件改动

**删除(McpServer 侧)**:
- `Tools/ParserTools.cs`、`Tools/RecordingTools.cs`、`Tools/AnalysisTools.cs`、`Tools/ModbusTools.cs`
- `ProxyClient.cs`
- 测试:`ParserToolsTests.cs`、`RecordingToolsTests.cs`、`ModbusToolsTests.cs`、`ProxyClientTests.cs`、`AnalysisToolsTests.cs`

**修改**:
- `Tools/SerialTools.cs` — 删除 8 个被裁工具方法 + 所有 `if (_ctx.UseProxy)` 分支 + MultiPort/AutoBaud/Stats 相关代码;保留 8 个核心工具
- `Tools/ToolContext.cs` — 保留 `Serial`、`Buffer`、`ParserManager`(构造函数必填参数,保留但不用于工具)、`UseProxy=false`;删除 `Proxy`、`Logger`、`Stats`、`MultiPort`、`AutoBaud`、`Recorder`、`Modbus`、`SlaveService`、`ConnectionManager` 属性及 `OnDataReceived` 订阅中的 Logger/Recorder/Stats 调用;删除 `JsonOpts` 中仅被删工具使用的部分(保留 RawJson 供 read_data 等用)
- `Program.cs` — 删除 proxy 分支(66-68 行)及自动启动 WPF 逻辑、删除 LoggerService/MultiPortService/AutoBaudDetector/Modbus 三件套/SessionRecorder 注册;`WithTools` 只留 `SerialTools`
- `TestHelpers/ToolContextFactory.cs` — 删除代理模式构建、删除被裁服务的 DI 注册
- `SerialToolsTests.cs`、`ToolContextTests.cs` — 删除针对被裁工具的测试

**注意**:Core 项目(WPF 共用)的 SerialService、LoggerService、MultiPortService、ModbusService 等**全部不动**,McpServer 侧只是不注册不引用。

### 文档同步

- `README.md` — 工具数 "39 个" → "8 个",删除代理模式/解析器相关描述
- `docs/guide/integration.md` — 删除 39 行工具速查表(373-413 行),改为 8 行;删除代理模式表格、AI 工作流中的 write_parser/activate_parser 步骤(保留串口工作流);"可用 MCP Tools(39 个)" → "(8 个)"
- `docs/guide/architecture.md` — 按文件标注工具数的段落(204-208 行)改为只列 SerialTools 8 个
- `docs/mcp-gap-analysis.md` — 归档文档,更新工具数引用(或保留,标注历史)

### 验收标准

1. `dotnet build -c Release`:0 警告 0 错误
2. `dotnet test`:全绿(删 5 个测试类后总数下降,剩余全部通过)
3. `dotnet run --project src/ACCcom.McpServer` 启动成功,`health_check` 外的 8 个工具可用(用 list_ports 验证)
4. 工作树干净,commit + push 到 GitHub