# 集成指南

ACCCOM 提供多种集成方式：内嵌 HTTP REST API、WebSocket 实时推送、MCP Server（AI 可直接调用）、国际化与主题切换。本文档涵盖所有外部集成接口的详细说明。

## HTTP API

内嵌 EmbedIO HTTP 服务，端口 **8899**。所有接口统一返回 JSON 格式：

```json
{
  "success": true,
  "error": null,
  "data": {}
}
```

- `success`：操作是否成功
- `error`：失败时的错误描述
- `data`：成功时的返回数据

### 串口管理

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

### 数据收发

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

### 协议解析器

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

### Modbus API

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/modbus/read` | 读取 Modbus 寄存器（01-04 功能码） |
| POST | `/api/modbus/write` | 写入 Modbus 寄存器/线圈（05-06、15-16、22-23 功能码） |
| POST | `/api/modbus/scan` | 扫描 Modbus 网络上的从站设备 |
| POST | `/api/slave/create` | 创建虚拟 Modbus 从站 |
| POST | `/api/slave/remove` | 移除 Modbus 从站 |
| GET | `/api/slaves` | 获取所有活跃的从站列表 |
| GET | `/api/slave/list` | 列出从站设备 |
| POST | `/api/slave/write` | 写入从站寄存器值 |
| POST | `/api/slave/read` | 读取从站寄存器值 |
| POST | `/api/baud/detect` | 自动探测设备波特率 |
| GET | `/api/statistics` | 获取接收速率、错误率、帧间隔等统计信息 |

```bash
# 读取保持寄存器
curl -X POST -H "Content-Type: application/json" \
  -d '{"slaveId":1,"functionCode":"ReadHoldingRegisters","startAddress":0,"quantity":10}' \
  http://127.0.0.1:8899/api/modbus/read

# 获取统计信息
curl http://127.0.0.1:8899/api/statistics

# 获取从站列表
curl http://127.0.0.1:8899/api/slaves
# → {"success":true,"data":[{"id":"slave_1","slaveId":1,"transportType":"tcp","connectionParam":"15000","isRunning":true}]}
```

### 录制 API

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/recording/start` | 开始录制串口通信到 JSONL 文件 |
| POST | `/api/recording/stop` | 停止当前录制 |
| GET | `/api/recording/status` | 查询录制状态 |
| POST | `/api/recording/replay` | 回放历史录制的会话文件 |

## WebSocket 实时推送

`HttpService` 内嵌 `SerialWebSocketHandler`，提供 WebSocket 端点 `/ws`：

- 数据到达时自动推送到所有已连接的 WebSocket 客户端
- 格式与 HTTP API `read_data` 一致
- 适合实时监控面板、Web 端数据流可视化
- 支持 Modbus 事务实时推送

## i18n 国际化

基于 `LanguageManager` 的多语言支持：

- 内置语言包：中文（`zh-CN.json`）、英文（`en-US.json`）
- 语言包存放于 `src/ACCcom/Languages/` 目录
- 运行时切换，无需重启
- 所有 UI 文本通过资源键引用
- Modbus 功能本地化支持

## 深色/浅色主题

基于 WPF ResourceDictionary 的主题切换：

- `DarkTheme.xaml`：深色主题
- `LightTheme.xaml`：浅色主题
- 资源文件存放于 `src/ACCcom/Themes/` 目录
- 运行时无缝切换，无需重启
- Modbus 窗口主题适配

## AI 集成

### 方案一：MCP Server（推荐）

ACCcom.McpServer 是一个独立进程的 MCP stdio 服务器，AI 客户端可直接启动并调用 38 个工具，无需 HTTP 配置。

**默认运行模式：**

| 模式 | 命令 | 说明 |
|------|------|------|
| 直连（默认） | `.\launch_acccom.ps1` 或 `dotnet run --project src/ACCcom.McpServer/ACCcom.McpServer.csproj` | 独立管理串口，无需桌面端，MCP 工具直接可用 |
| 代理（需桌面端） | `.\launch_acccom_gui.ps1` | 单独启动 WPF 桌面端用于可视化监控 |

> MCP Server 默认以**直接模式**运行，串口工具始终可用且不会弹出 GUI。

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

**AI 自动化 Modbus 调试工作流：**

```
读取 Modbus 设备文档
  → read_registers(slaveId=1, functionCode="03", startAddress=0, quantity=10)  # 读取保持寄存器
  → write_register(slaveId=1, functionCode="06", address=0, value=100)        # 写入单个寄存器
  → slave_create(slaveId=1, transport="tcp", connectionParam="15000")         # 创建虚拟从站
  → slave_write(slaveId="slave_1", type="holding", address=0, value=100)      # 写入从站寄存器
  → slave_read(slaveId="slave_1", type="holding", address=0)                  # 读取从站寄存器
```

**可用 MCP Tools（38 个）：**

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
| `generate_parser` | 根据协议 schema JSON 自动生成 .csx 解析器脚本 |
| `validate_parser` | 校验协议 schema JSON（不生成脚本） |
| `get_schema_template` | 获取协议 schema JSON 模板 |
| `analyze_protocol` | 批量分析协议数据（字段统计、错误分布、解析结果） |
| `compare_frames` | 逐帧对比两组 Hex 数据的字段差异 |
| `start_recording` | 开始录制串口通信到 JSONL 文件 |
| `stop_recording` | 停止当前录制 |
| `replay_session` | 回放历史录制的会话文件 |
| `recording_status` | 查询当前录制状态（是否在录制、文件路径、已录制条数） |
| `read_registers` | 读取 Modbus 寄存器（支持 01-04 功能码） |
| `write_register` | 写入 Modbus 寄存器/线圈（支持 05-06、15-16、22-23 功能码） |
| `slave_create` | 创建虚拟 Modbus 从站设备 |
| `slave_remove` | 移除 Modbus 从站设备 |
| `slave_list` | 列出所有活跃的 Modbus 从站设备 |
| `slave_write` | 向从站设备写入寄存器值 |
| `slave_read` | 从从站设备读取寄存器值 |
| `scan_devices` | 扫描 Modbus 网络上的从站设备 |

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
- Modbus 事务日志格式：`[时间戳] Slave=0x01 FC=0x03 Req=... Resp=... Status=OK`

## 相关文档

- [架构指南](../guide/architecture.md)
