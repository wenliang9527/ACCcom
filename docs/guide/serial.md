# 串口操作指南

ACCCOM 提供了完整的串口配置、数据收发、文件操作和连接管理功能，覆盖桌面和 MCP/HTTP API 两种操作模式。

## 串口配置

- 端口、波特率（300~921600）、数据位、停止位、校验位
- DTR/RTS 流控
- 自动扫描可用串口

## 数据收发

- 双窗口独立显示：接收区 (RX) / 发送区 (TX)，带时间戳、方向、原始 HEX、文本
- HEX / ASCII 发送切换
- RX/TX 数据统计（帧数 + 字节数）
- 实时连接时长显示 (hh:mm:ss)
- 发送数据自动回显到 TX 区
- 快捷发送栏（预设 AT 指令，支持自定义添加/删除，持久化到 `shortcuts.json`）
- 循环发送（可设间隔，毫秒级，支持停止）
- 发送历史（上下键翻阅）
- RX/TX 面板实时关键字过滤（忽略大小写）
- 断线自动重连（最多 10 次，1 秒间隔）
- 深色/浅色主题切换
- 支持 Modbus 寄存器读写操作

## 文件操作

- RX/TX 数据一键保存 `.txt`
- 收发日志自动写入 `logs/` 目录，自动轮转（单文件 5MB）
- Modbus 事务日志导出

## 串口连接管理器

`SerialConnectionManager` 管理串口连接生命周期：

- 连接/断开切换（`ToggleConnection`）
- 连接时长实时追踪（每秒触发 `DurationChanged` 事件）
- 格式化时长显示（`hh:mm:ss`）
- 支持 Modbus 连接管理

## 相关文档

- [快速入门](../guide/getting-started.md)
- [高级通信](../guide/advanced-comms.md) — 多端口并发、网络桥接、自动波特率检测
- [数据管理](../guide/data-management.md)
