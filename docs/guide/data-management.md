# 数据管理

ACCCOM 内置高性能数据缓冲系统、实时统计引擎和多格式导出服务，帮助用户高效管理和分析串口收发数据。

## 数据缓冲区

高性能数据缓冲系统，采用预分配 `RingBuffer` 环形缓冲 + 等待者队列架构：

- `RingBuffer`（默认容量 10000 条）：固定大小环形缓冲，O(1) 写入，快照读取
- 支持 `WaitForEntry` 阻塞等待匹配数据（用于 HTTP `POST /api/wait-for` / MCP `wait_for_response`）
- 支持增量拉取（`sinceId`）、方向过滤（RX/TX）、关键字过滤

## 数据统计

`DataStatistics` 实时统计串口数据性能指标：

- `RxBytesPerSecond`：接收速率（字节/秒）
- `RxFramesPerSecond`：接收帧率（帧/秒）
- `ErrorRate`：错误率（%）
- `AvgFrameIntervalMs`：平均帧间隔（毫秒）
- 基于预分配 `SampleRing`（容量 16384）的滑动窗口计算（保留最近 5 秒样本）
- 可通过 HTTP API 获取实时统计：`GET /api/statistics`

## 数据导出

`FileExportService` 支持多种格式导出收发数据：

- **TXT**：`[时间戳][方向] RawHex | Text` 纯文本格式
- **JSON**：结构化 JSON 数组，包含 timestamp、direction、hex、text、fields
- **CSV**：逗号分隔格式，适合 Excel 打开
- **PCAP**：PCAP 格式导出，可用 Wireshark 打开分析（`PcapExportService`）
- 支持 RX/TX 分别导出

## 相关文档

- [串口操作指南](../guide/serial.md)
- [可视化](../guide/visualization.md) — 实时绘图、统计仪表盘、数据对比与差异
