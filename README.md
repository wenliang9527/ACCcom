# ACCCOM 串口调试工具

Windows 桌面串口调试工具，对标 SSCOM 5.13.1，内置 HTTP API 供外部工具读写串口数据。

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
  -d '{"port":"COM3","baudRate":115200,"dataBits":8,"stopBits":1,"parity":0,"dtr":false,"rts":false}' \
  http://127.0.0.1:8899/api/port/open
```

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
  -d '{"data":"AT+GMR","isHex":false}' \
  http://127.0.0.1:8899/api/send
```

**发送 HEX 数据：**

```bash
curl -X POST -H "Content-Type: application/json" \
  -d '{"data":"41 54 2B 47 4D 52 0D 0A","isHex":true}' \
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
  -d '{"pattern":"OK","timeoutMs":5000,"matchMode":"contains","direction":"RX"}' \
  http://127.0.0.1:8899/api/wait-for

# 正则匹配
curl -X POST -H "Content-Type: application/json" \
  -d '{"pattern":"AT\\+\\w+","timeoutMs":3000,"matchMode":"regex"}' \
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
  -d '{"target":"rx"}' http://127.0.0.1:8899/api/clear
# target: "rx" | "tx" | "all"（默认 all）
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
  -d '{"name":"my-protocol"}' http://127.0.0.1:8899/api/parser/activate

# 停用
curl -X POST -H "Content-Type: application/json" \
  -d '{"name":null}' http://127.0.0.1:8899/api/parser/activate
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

## opencode 集成方案

opencode 等 AI 编码工具可通过以下两种方案与 ACCCOM 串口交互，实现 AI 自动化串口调试。

### 方案一：HTTP API（推荐）

ACCCOM 启动时自动监听 `http://127.0.0.1:8899`，opencode 通过 MCP 或直接 `Invoke-RestMethod` 访问。

**配置 opencode MCP 服务器（`opencode.json`）：**

```json
{
  "mcpServers": {
    "acccom-serial": {
      "type": "url",
      "url": "http://127.0.0.1:8899",
      "tools": {
        "listPorts": { "method": "GET", "path": "/api/ports" },
        "getStatus": { "method": "GET", "path": "/api/status" },
        "openPort": { "method": "POST", "path": "/api/port/open" },
        "closePort": { "method": "POST", "path": "/api/port/close" },
        "sendData": { "method": "POST", "path": "/api/send" },
        "readData": { "method": "GET", "path": "/api/data?since=0" },
        "waitForResponse": { "method": "POST", "path": "/api/wait-for" },
        "clearBuffer": { "method": "POST", "path": "/api/clear" },
        "listParsers": { "method": "GET", "path": "/api/parsers" },
        "activateParser": { "method": "POST", "path": "/api/parser/activate" },
        "healthCheck": { "method": "GET", "path": "/api/health" }
      }
    }
  }
}
```

**无 MCP 时直接用 PowerShell：**

```powershell
# 打开串口
Invoke-RestMethod -Uri "http://127.0.0.1:8899/api/port/open" -Method Post -ContentType "application/json" -Body '{"port":"COM3","baudRate":115200}'

# 发送 AT 指令并等待响应
Invoke-RestMethod -Uri "http://127.0.0.1:8899/api/send" -Method Post -ContentType "application/json" -Body '{"data":"AT+GMR"}'
Invoke-RestMethod -Uri "http://127.0.0.1:8899/api/wait-for" -Method Post -ContentType "application/json" -Body '{"pattern":"OK","timeoutMs":3000}'

# 增量读取接收数据
$data = Invoke-RestMethod -Uri "http://127.0.0.1:8899/api/data?since=0"
$data.data.entries | ForEach-Object { "[$($_.timestamp)][$($_.direction)] $($_.text)" }

# 查看状态
Invoke-RestMethod -Uri "http://127.0.0.1:8899/api/status"

# 关闭串口
Invoke-RestMethod -Uri "http://127.0.0.1:8899/api/port/close" -Method Post

# 列出串口
Invoke-RestMethod -Uri "http://127.0.0.1:8899/api/ports"
```

**opencode 工作流示例：**

```
你：打开 COM3 串口，波特率 115200
opencode：→ POST /api/port/open → 确认连接成功

你：发送 AT+GMR 并等待设备回复
opencode：→ POST /api/send {"data":"AT+GMR"}
         → POST /api/wait-for {"pattern":"OK","timeoutMs":5000}
         → 分析返回数据，回复固件版本

你：切换到 my-protocol 解析器
opencode：→ POST /api/parser/activate {"name":"my-protocol"} → 确认激活

你：清空缓冲区，重新读取最新数据
opencode：→ POST /api/clear {"target":"all"}
         → GET /api/data?since=0 → 返回新数据
```

### 方案二：文件监控

ACCCOM 将收发数据自动写入 `logs/ACCCOM_yyyyMMdd_HHmmss.log`，opencode 通过文件读取获取数据。

```powershell
# 查找最新日志文件
$log = Get-ChildItem -Path "ACCcom/src/ACCcom/bin/Debug/net8.0-windows/logs/" -Filter "*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1

# 读取日志内容
Get-Content $log.FullName -Tail 50
```

如需从 opencode 发送数据到串口，ACCCOM 可监控 `send.txt` 文件（需自行实现 `FileSystemWatcher`）：

```
logs/ACCCOM_20260529_143022.log   # 接收日志（自动生成）
send.txt                        # ACCCOM 监控此文件，有新内容时发送（需手动创建）
```

### 数据格式

**日志/API 输出格式：**

```
[14:30:22.123][RX] 41 54 2B 47 4D 52 0D 0A | AT+GMR
[14:30:22.456][TX] 41 54 0D 0A | AT
```

## 许可

MIT
