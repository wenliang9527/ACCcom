# ACCCOM — 串口调试工具设计文档

## 1. 概述

ACCCOM 是一款 Windows 桌面串口调试工具，对标 SSCOM 5.13.1 的核心功能，使用 C# WPF (.NET 8) 开发。内置 HTTP API 服务，允许 opencode 等外部工具通过文件 + REST 接口与串口数据交互。

### 设计目标

- **轻量**：安装包 < 10MB，内存占用 < 60MB
- **核心功能完整**：覆盖 SSCOM 日常使用场景
- **可编程接口**：HTTP API 供外部程序读写串口
- **Win 原生**：WPF 原生体验，响应式 UI

## 2. 技术栈

| 层级 | 技术 | 说明 |
|------|------|------|
| 桌面框架 | WPF (.NET 8) | 原生 Windows 桌面应用 |
| 串口通信 | System.IO.Ports | .NET 内置串口库 |
| HTTP Server | EmbedIO | 轻量级嵌入 HTTP 库 |
| 数据可视化 | WPF 原生控件 + ScottPlot (可选) | 波形显示预留 |
| 打包 | MSBuild + 单文件发布 | 单个 exe 部署 |

## 3. 架构

```
┌─────────────────────────────────────────────────┐
│                  ACCCOM WPF App                     │
│                                                   │
│  ┌──────────┐   ┌──────────────┐   ┌──────────┐  │
│  │   UI     │   │ SerialService│   │  Logger   │  │
│  │ (XAML)  │◄─►│ (SerialPort  │──►│(File/     │  │
│  │          │   │  Manager)    │   │ Console)  │  │
│  └──────────┘   └──────┬───────┘   └──────────┘  │
│                        │                          │
│               ┌────────▼────────┐                 │
│               │   HttpService   │                 │
│               │   (EmbedIO)     │                 │
│               │   :8899         │                 │
│               └────────┬────────┘                 │
└────────────────────────┼──────────────────────────┘
                         │
              ┌──────────▼──────────┐
              │  opencode / curl    │
              │  (外部工具)          │
              └─────────────────────┘
```

### 模块说明

| 模块 | 职责 |
|------|------|
| **UI** | WPF 窗口，串口配置、收发显示、快捷发送等 |
| **SerialService** | 串口打开/关闭/读写，线程管理，跨线程通知 |
| **Logger** | 收发数据记录到文件，支持日志轮转 |
| **HttpService** | EmbedIO 监听 `127.0.0.1:8899`，提供 REST API |

## 4. 功能列表

### 4.1 串口配置
- 端口号、波特率、数据位、停止位、校验位
- DTR/RTS 控制
- 自动扫描可用端口（刷新按钮）
- **断开自动重连**：串口意外断开后可选择自动重连（可配置重连间隔和次数）

### 4.2 数据收发与时间戳
- **双窗口独立显示**：接收区 (RX) 和发送区 (TX) 分开显示，各自独立控制清空、格式切换
- **显示格式**：ASCII / HEX / UTF-8 / GBK 切换
- **时间戳**：每条收发数据可选加时间戳前缀（接收和发送可独立开关）
- **时间戳格式**：`[HH:mm:ss.fff]` 或 `[yyyy-MM-dd HH:mm:ss.fff]` 可选
- **自动换行**：按接收间隔或按 `\n` 断行
- **自动滚动**：保持滚动到底部 / 手动滚动暂停自动滚动
- **统计**：RX/TX 字节计数、速率显示

### 4.3 数据发送
- 发送区：单行发送 / 多行发送
- HEX 发送 / ASCII 发送 切换
- **发送数据自动回显**：发送的内容自动显示在 TX 区，方便回溯
- 自动追加回车换行（CR/LF/CR+LF 可选）
- 循环发送：设置间隔，定时发送
- 快捷发送栏：可配置多条预设指令

### 4.4 文件传输
- 发送文件到串口（可选，SSCOM 兼容）
- **保存窗口数据**：接收区或发送区的内容一键保存为 `.txt` 文件
- 接收数据自动记录到日志文件（持续写入）

### 4.5 HTTP API（供外部工具使用）

所有接口统一返回 `{ "success": bool, "error"?: string, "data"?: object }`。

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/ports` | 列出可用串口 |
| GET | `/api/status` | 连接状态、配置、RX/TX 计数 |
| POST | `/api/port/open` | 打开串口（JSON 参数） |
| POST | `/api/port/close` | 关闭串口 |
| POST | `/api/send` | 发送数据到串口 |
| GET | `/api/data` | 获取收发数据（since/limit/direction 过滤） |
| POST | `/api/wait-for` | 阻塞等待匹配数据（超时返回） |
| POST | `/api/clear` | 清空数据缓冲区 |
| GET | `/api/parsers` | 列出解析器及当前激活项 |
| POST | `/api/parser/activate` | 激活/停用解析器 |
| GET | `/api/health` | 健康检查 |

### 4.6 快捷键
- **ESC**：清空当前激活的窗口（RX 或 TX）
- **Ctrl+S**：保存当前激活窗口数据到文件
- **Enter**：发送当前输入框内容
- **Ctrl+Enter**：发送并换行

### 4.7 日志
- 收发日志自动保存到 `logs/` 目录
- 文件名格式：`ACCCOM_yyyyMMdd_HHmmss.log`
- 单文件大小限制，自动轮转

## 5. UI 布局

```
┌─────────────────────────────────────────────────────┐
│  [端口▼] [波特率▼] [数据位▼] [校验位▼] [停止位▼]      │
│  [打开串口]  [DTR] [RTS]  [刷新]                    │
├────────────────────────────┬────────────────────────┤
│        接收区 (RX)          │      发送区 (TX)        │
│  ☑时间戳  HEX  ASC  清空 保存│  ☑时间戳  HEX  ASC  清空 保存│
│                            │                        │
│                            │                        │
│                            │                        │
│                            │                        │
│                            │                        │
├────────────────────────────┴────────────────────────┤
│  [快捷发送 ▼]  ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐ │
│                │AT+GMR│ │AT+RST│ │AT+CGI│ │  +  │ │
│                └──发送─┘ └──发送─┘ └──发送─┘ └────┘ │
│  [发送区: 单行输入框 / HEX/ASCII切换]  [AT↵] [发送]   │
│  [循环发送] ☐ [间隔: 1000ms] [停止]                  │
├─────────────────────────────────────────────────────┤
│  状态栏: COM15 已开启 | 115200 | RX: 1024 | TX: 512  │
│          HTTP API: http://127.0.0.1:8899              │
└─────────────────────────────────────────────────────┘
```

## 6. 数据流

### 6.1 接收流程
```
串口设备 ──→ SerialPort.DataReceived
                │
                ▼
          SerialService.OnDataReceived (工作线程)
                │
         ┌──────┼──────────┐
         ▼      ▼          ▼
      UI队列  HttpService  Logger
    (Dispatcher) (缓存)   (写入文件)
         │
         ▼
      䛫收区展示
```

### 6.2 发送流程
```
用户输入 / HTTP POST / 快捷发送
        │
        ▼
   SerialService.Send(data)
        │
        ▼
   SerialPort.Write
        │
   ┌────┴────┐
   ▼         ▼
 Logger    UI回显
```

### 6.3 文件方案
- 接收数据实时写入 `logs/ACCCOM_yyyyMMdd_HHmmss.log`
- opencode 通过 `Read` 工具读取日志文件
- 可选：opencode 写 `send.txt`，App 用 `FileSystemWatcher` 监控并发送

## 7. 项目结构

```
ACCcom/
├── ACCcom.sln
├── src/
│   └── ACCcom/
│       ├── ACCcom.csproj
│       ├── App.xaml / App.xaml.cs
│       ├── MainWindow.xaml / MainWindow.xaml.cs
│       ├── Models/
│       │   └── SerialConfig.cs
│       │   └── ReceivedData.cs
│       ├── Services/
│       │   ├── SerialService.cs      # 串口读写管理
│       │   ├── LoggerService.cs      # 文件日志
│       │   └── HttpService.cs        # EmbedIO REST API
│       ├── ViewModels/
│       │   └── MainViewModel.cs
│       └── Views/
│           └── (自定义控件, 如有)
├── docs/
│   └── design/
│       └── 2026-05-29-acccom-serial-debugger-design.md
└── README.md
```

## 8. 未涵盖范围（后续可加）

- Modbus 协议解析
- TCP/UDP 网络串口转发
- 波形图实时显示
- Lua 脚本自动化

## 9. 附录：EmbedIO 引用

```xml
<PackageReference Include="EmbedIO" Version="3.*" />
```

NuGet 包大小约 150KB，0 外部依赖，MIT 许可。
