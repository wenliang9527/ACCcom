# 快速入门

ACCCOM 是一款 Windows 桌面串口调试工具，对标 SSCOM 5.13.1。支持自定义协议解析脚本（C# Script）、HTTP API、MCP Server（AI 可直接调用），实现从"看原始 Hex"到"看懂数据含义"的完整闭环。

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

## 快捷键

| 按键 | 行为 |
|------|------|
| Enter | 发送输入框内容 |
| Ctrl+Enter | 插入换行 |
| Ctrl+S | 保存接收区数据 |
| ESC | 清空 RX/TX |
| F5 | 打开 Modbus 窗口 |
| F6 | 打开实时绘图窗口 |
| F7 | 打开统计仪表盘 |
| F8 | 打开会话回放窗口 |

## 相关文档

- [串口操作指南](../guide/serial.md)
- [数据管理](../guide/data-management.md)
