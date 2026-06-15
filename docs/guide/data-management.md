# 数据管理

ACCCOM 内置高性能数据缓冲系统、实时统计引擎和多格式导出服务，帮助用户高效管理和分析串口收发数据。

## 数据缓冲区

高性能数据缓冲系统，采用 `Channel<T>` + `RingBuffer` 双层架构：

- `Channel<LogEntry>`：无锁异步管道，支持多读多写，用于事件分发
- `RingBuffer`（默认容量 10000 条）：固定大小环形缓冲，O(1) 写入，快照读取
- 支持 `WaitForEntry` 阻塞等待匹配数据（用于 `wait-for` API / MCP `wait_for_response`）
- 支持增量拉取（`sinceId`）、方向过滤（RX/TX）、关键字过滤
- 支持 Modbus 事务数据记录和查询

## 数据统计

`DataStatistics` 实时统计串口数据性能指标：

- `RxBytesPerSecond`：接收速率（字节/秒）
- `RxFramesPerSecond`：接收帧率（帧/秒）
- `ErrorRate`：错误率（%）
- `AvgFrameIntervalMs`：平均帧间隔（毫秒）
- 基于 `ConcurrentQueue` 的滑动窗口计算（保留最近 10 秒样本）
- MCP 工具：`get_statistics`
- Modbus 事务统计：成功/失败计数、响应时间、异常分布

## 数据导出

`FileExportService` 支持多种格式导出收发数据：

- **TXT**：`[时间戳][方向] RawHex | Text` 纯文本格式
- **JSON**：结构化 JSON 数组，包含 timestamp、direction、hex、text、fields
- **CSV**：逗号分隔格式，适合 Excel 打开
- 支持 RX/TX 分别导出
- 支持 Modbus 事务日志导出

## 相关文档

- [串口操作指南](../guide/serial.md)
- [可视化](../guide/visualization.md) — 实时绘图、统计仪表盘、数据对比与差异
