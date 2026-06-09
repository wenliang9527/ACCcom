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
- 双窗口独立显示：接收区 (RX) / 发送区 (TX)
- HEX / ASCII 显示切换
- 时间戳独立开关
- 发送数据自动回显到 TX 区
- 快捷发送栏（预设 AT 指令，可添加）
- 循环发送（可设间隔）
- 支持 HEX 发送
- 支持多行发送

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

内置辅助函数：`RawHex`, `ToUInt16`, `ToInt16`, `ToFloat`, `Crc16`, `Sum8`, `Xor8`

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
| POST | `/api/parser/activate` | 激活/停用指定解析器 |

```bash
# 列出解析器
curl http://127.0.0.1:8899/api/parsers

# 激活解析器
curl -X POST -H "Content-Type: application/json" \
  -d '{"Name":"my-protocol"}' http://127.0.0.1:8899/api/parser/activate

# 停用
curl -X POST -H "Content-Type: application/json" \
  -d '{"Name":null}' http://127.0.0.1:8899/api/parser/activate
```

### 快捷键

| 按键 | 行为 |
|------|------|
| Enter | 发送输入框内容 |
| Ctrl+Enter | 插入换行 |
| Ctrl+S | 保存接收区数据 |
| ESC | 清空 RX/TX |

## 技术栈

| 层级 | 技术 |
|------|------|
| 桌面框架 | WPF (.NET 8) |
| 串口通信 | System.IO.Ports |
| HTTP 服务 | EmbedIO |
| 架构 | MVVM |

## 项目结构

```
ACCcom/
├── ACCcom.sln
├── launch_acccom.ps1         # 一键启动脚本（WPF + MCP Server 代理模式）
├── src/
│   ├── ACCcom.Core/              # 共享核心库（net8.0，无 WPF 依赖）
│   │   ├── Models/
│   │   │   ├── ApiResponse.cs       # 统一 API 响应格式
│   │   │   ├── ApiRequests.cs       # 请求体模型
│   │   │   ├── SerialConfig.cs
│   │   │   ├── LogEntry.cs
│   │   │   └── FieldAnnotation.cs
│   │   ├── Services/
│   │   │   ├── SerialService.cs     # 串口管理
│   │   │   ├── HttpService.cs       # HTTP REST API (EmbedIO)
│   │   │   ├── LoggerService.cs     # 文件日志
│   │   │   ├── ParserEngine.cs      # Roslyn .csx 脚本引擎
│   │   │   ├── ParserManager.cs     # 解析器热加载管理
│   │   │   └── ScriptGlobals.cs     # .csx 脚本辅助方法
│   │   └── parsers/sample.csx
│   ├── ACCcom/                     # WPF 桌面客户端
│   │   ├── App.xaml / MainWindow.xaml
│   │   ├── ViewModels/
│   │   └── Converters/
│   └── ACCcom.McpServer/          # MCP Server (stdio 传输)
│       └── Program.cs              # 15 个 MCP 工具
├── docs/
│   ├── design/
│   └── plan/
└── README.md
```

## AI 集成

### 方案一：MCP Server（推荐）

ACCcom.McpServer 是一个独立进程的 MCP stdio 服务器，AI 客户端可直接启动并调用 12 个工具，无需 HTTP 配置。

**两种运行模式：**

| 模式 | 命令 | 说明 |
|------|------|------|
| 直连 | `dotnet run --project src/ACCcom.McpServer/ACCcom.McpServer.csproj` | 独立管理串口，无需桌面端 |
| 代理（推荐） | `.\launch_acccom.ps1` | 自动启动 WPF 桌面端 + MCP Server，通过 HTTP API 操作串口，数据实时同步到 GUI |

**推荐使用代理模式**：所有串口操作转发给 ACCCOM WPF 的 HTTP API（端口 8899），AI 操作和桌面端实时同步。

```powershell
# 代理模式（自动启动 WPF 桌面端 + MCP Server）
.\launch_acccom.ps1

# 直连模式（无桌面端）
dotnet run --project src/ACCcom.McpServer/ACCcom.McpServer.csproj

# 代理模式（手动指定 API 地址）
.\launch_acccom.ps1 --proxy-url http://127.0.0.1:8899
```

> 在 OpenCode 中，MCP Server 配置已自动使用 `launch_acccom.ps1` 启动脚本，确保 WPF 桌面端先于 MCP Server 运行，代理模式开箱即用。

**AI 自动化串口调试完整工作流：**

```
阅读设备协议文档
  → write_parser("my-device", .csx脚本代码)     # AI 生成解析脚本
  → activate_parser("my-device")                 # 激活
  → open_port(port="COM3", baudRate=115200)     # 打开串口
  → send(data="AT+GMR")                         # 发送指令
  → read_data(sinceId=0, direction="RX")        # 读取响应
  → parse_raw(hex="AA 55 03 01 19 2E")          # 离线调试解析结果
```

**可用 MCP Tools：**

| Tool | 说明 |
|------|------|
| `list_ports` | 列出可用串口 |
| `get_status` | 连接状态、配置、收发计数 |
| `open_port` | 打开串口（波特率、数据位、停止位、校验位、DTR/RTS） |
| `close_port` | 关闭串口 |
| `send` | 发送数据（ASCII 或 HEX） |
| `read_data` | 增量读取缓冲数据（支持 sinceId / limit / direction 过滤） |
| `wait_for_response` | 阻塞等待匹配数据（支持 contains / regex / exact 匹配，可超时） |
| `clear_buffer` | 清空缓冲区（rx/tx/all） |
| `list_parsers` | 列出可用协议解析器 |
| `read_parser` | 读取解析器源码 |
| `write_parser` | 写入/更新 .csx 解析器脚本（AI 生成脚本的关键入口） |
| `activate_parser` | 激活/停用解析器 |
| `parse_raw` | 离线解析 Hex 数据（无需串口，用于验证解析器是否正确） |

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
