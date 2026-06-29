# 自动化系统

ACCCOM 提供了会话录制回放、宏引擎和触发器系统三大自动化能力，支持从数据采集到自动响应的完整闭环。

## 会话录制

`SessionRecorder` 将串口通信完整录制到 JSONL 文件：

- **格式**：每行一个 JSON 对象，包含 `id`、`timestamp`、`direction`、`rawHex`、`text`、`fields`
- **异步写入**：基于 `BufferedFileWriter` 实现，2 秒定时器 + 100 写计数器批量刷盘，避免频繁 IO
- **自动目录**：录制文件自动创建在 `recordings/` 目录下
- **MCP 工具**：`start_recording`、`stop_recording`、`replay_session`、`recording_status`
- **Modbus 支持**：支持 Modbus 事务录制

## 会话回放

`ReplayWindow` 回放历史录制的会话文件：

- **加载**：支持加载 JSONL 格式的录制文件
- **逐条回放**：按原始顺序逐条还原数据及解析结果
- **MCP 工具**：`replay_session`
- **Modbus 支持**：支持 Modbus 事务回放

## 宏引擎

`MacroManager` 管理多步骤宏模板（`MacroTemplate`），支持批量自动化操作：

- **多步骤**：每个宏包含多个步骤，每步定义命令、是否 HEX 发送、步骤间延时
- **变量替换**：支持模板变量动态替换
- **异步执行**：支持异步执行，可随时取消
- **持久化**：宏模板持久化到 `macros.json`
- **Modbus 支持**：支持 Modbus 命令序列

## 触发器系统

`TriggerService` 基于规则的数据触发引擎：

- **规则定义**：每条规则（`TriggerRule`）包含名称、方向过滤、匹配模式（contains / regex）、是否匹配 HEX、是否启用
- **自动评估**：数据到达时逐条评估匹配规则，触发 `OnTriggerFired` 事件
- **联动执行**：可联动自动回复、日志记录等自动化动作
- **Modbus 支持**：支持 Modbus 事务触发

## 相关文档

- [串口操作指南](../guide/serial.md)
- [管理功能](../guide/management.md)
