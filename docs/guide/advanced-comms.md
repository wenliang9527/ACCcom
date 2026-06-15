# 高级通信功能

ACCCOM 在基础串口通信之上提供了多端口并发、网络桥接、自动波特率检测和虚拟串口服务，满足复杂的调试场景需求。

## 多端口并发

`MultiPortService` 支持同时打开多个串口，每个端口独立管理生命周期：

- 按标签（tag）标识每个端口实例
- 统一事件总线：所有端口的数据通过 `OnDataReceived` 事件汇总，自动附加 `PortTag` 标识
- 独立的连接/断开/错误事件
- MCP 工具：`open_port_tagged`、`close_port_tagged`、`send_to_port_tagged`
- 支持同时连接多个设备进行数据对比分析

## 网络桥接

`NetworkBridgeService` 支持 TCP/UDP 网络连接，将网络数据桥接到串口数据流中：

- TCP 客户端：`ConnectTcp(host, port)` — 建立 TCP 连接，自动接收循环
- UDP 客户端：`ConnectUdp(host, port)` — 建立 UDP 连接，支持异步接收
- 零拷贝接收：使用 `ArrayPool<byte>` + buffer+offset+count 直接传递，消除每包 byte[] 分配
- 网络数据统一转为 `LogEntry` 事件，与串口数据共用同一缓冲区和解析管道
- 自动重连和断线检测
- 支持 Modbus TCP 传输层

## 自动波特率检测

`AutoBaudDetector` 自动探测设备波特率：

- 按优先级依次尝试：9600 → 115200 → 57600 → 38400 → 19200 → 4800 → 2400 → 1200 → 460800 → 230400 → 921600
- 发送探测字节（0x00 × 3），检测是否有响应
- MCP 工具：`detect_baud_rate`
- 支持 Modbus 设备波特率检测

## 虚拟串口服务

`VirtualSerialService` 提供虚拟串口功能，用于测试和调试：

- 模拟串口连接/断开
- 发送数据记录（TX 方向）
- 注入 RX 数据（模拟设备响应）
- 无需真实硬件即可测试协议解析器

## Related

- [串口操作指南](../guide/serial.md)
- [协议解析文档](../guide/protocol-parsing.md)
- [Modbus 支持](../guide/modbus.md)
