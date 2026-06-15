<div align="center">

# ACCCOM 串口调试工具

**从"看原始 Hex"到"看懂数据含义"的完整闭环**

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/WPF-.NET_8-512BD4?logo=windows)](https://github.com/dotnet/wpf)
[![MCP](https://img.shields.io/badge/MCP-Server-4A5568?logo=serverfault)](https://modelcontextprotocol.io)
[![License](https://img.shields.io/badge/License-MIT-green)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-421_passing-22C55E)](https://github.com/)

Windows 桌面串口调试工具，支持自定义 C# Script 协议解析、HTTP API、AI MCP Server，对标 SSCOM 5.13.1。

</div>

---

## 快速上手

```bash
# 编译全部项目
dotnet build ACCcom.sln

# 运行 WPF 桌面客户端
dotnet run --project src\ACCcom\ACCcom.csproj

# 运行 MCP Server（供 AI 客户端直接调用）
dotnet run --project src\ACCcom.McpServer\ACCcom.McpServer.csproj

# 发布单文件
dotnet publish src\ACCcom\ACCcom.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist\
```

<details>
<summary><b>系统要求</b></summary>

| 项目 | 要求 |
|------|------|
| 操作系统 | Windows 10 1607+ / Server 2019+ |
| 运行时 | .NET 8 运行时（[下载](https://dotnet.microsoft.com/download/dotnet/8.0)） |
| 编译环境 | .NET 8 SDK |
| 架构 | x64 |

</details>

---

## 文档索引

### 🚀 入门

| 文档 | 内容 |
|------|------|
| [快速入门](docs/guide/getting-started.md) | 系统要求、安装方法、快捷键一览 |

### 🔌 串口通信

| 文档 | 内容 |
|------|------|
| [串口操作指南](docs/guide/serial.md) | 串口配置、数据收发、文件操作、连接管理器 |
| [数据管理](docs/guide/data-management.md) | 高性能数据缓冲区、实时统计、多格式导出 |
| [高级通信](docs/guide/advanced-comms.md) | 多端口并发、TCP/UDP 桥接、自动波特率、虚拟串口 |

### 📋 协议解析

| 文档 | 内容 |
|------|------|
| [协议解析引擎](docs/guide/protocol-parsing.md) | C# Script 解析器、可视化编辑器、多帧拼接、测试运行器 |
| [协议→解析器 操作指南](docs/protocol-to-parser.md) | 从协议文档到生成 .csx 解析器的完整流程 |

### 🔧 Modbus

| 文档 | 内容 |
|------|------|
| [Modbus 支持](docs/guide/modbus.md) | 图形界面操作、功能码速查、设备扫描、从站模拟、MCP 工具 |

### ⚡ 自动化

| 文档 | 内容 |
|------|------|
| [自动化系统](docs/guide/automation.md) | 会话录制回放、多步骤宏、条件触发 |
| [管理功能](docs/guide/management.md) | 书签标记、配置预设、快捷指令、全局设置 |

### 📊 可视化

| 文档 | 内容 |
|------|------|
| [可视化分析](docs/guide/visualization.md) | 实时波形绘图、统计仪表盘、数据对比、差异分析 |

### 🔗 集成与架构

| 文档 | 内容 |
|------|------|
| [集成指南](docs/guide/integration.md) | HTTP API（完整参考）、WebSocket、国际化、主题、AI 集成 |
| [架构指南](docs/guide/architecture.md) | MVVM 分层、基类体系、性能优化策略、技术栈、项目结构 |

---

## 功能速览

| 类别 | 功能 |
|------|------|
| 🔌 串口 | 300~921600 波特率、DTR/RTS 流控、自动扫描、断线重连 |
| 📡 网络 | TCP/UDP 客户端、自动桥接、零拷贝接收 |
| 📝 协议 | Roslyn C# Script 引擎、热加载、LRU 缓存、自动代码生成 |
| 🔧 Modbus | RTU/TCP 主站、10 种功能码、自动分片、轮询、从站模拟 |
| 🎨 界面 | 深色/浅色主题、中英文切换（运行时即时生效） |
| 🤖 AI | MCP Server（34 个工具）、HTTP REST API、WebSocket 实时推送 |
| 📊 数据 | Channel+RingBuffer 缓冲、实时统计、TXT/JSON/CSV 导出 |
| ⚡ 自动化 | 会话录制回放、多步骤宏、条件触发器、协议测试运行器 |

---

## 快捷键

| 按键 | 行为 |
|------|------|
| `Enter` | 发送 |
| `Ctrl+Enter` | 换行 |
| `Ctrl+S` | 保存 RX |
| `ESC` | 清空面板 |
| `F5` | 打开 Modbus |
| `F6` | 打开绘图 |
| `F7` | 打开统计 |
| `F8` | 打开回放 |

---

## 许可

[MIT](LICENSE)
