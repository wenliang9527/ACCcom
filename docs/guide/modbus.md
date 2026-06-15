# Modbus 支持

`ModbusService` 提供完整的 Modbus RTU/TCP 主站功能，支持所有标准功能码：

- **读操作**：ReadCoils (0x01)、ReadDiscreteInputs (0x02)、ReadHoldingRegisters (0x03)、ReadInputRegisters (0x04)
- **写操作**：WriteSingleCoil (0x05)、WriteSingleRegister (0x06)、WriteMultipleCoils (0x0F)、WriteMultipleRegisters (0x10)
- **高级操作**：MaskWriteRegister (0x16)、ReadWriteMultipleRegisters (0x17)
- **传输层**：支持 RTU（串口）和 TCP（网络）两种传输方式
- **自动分片**：大范围读取自动拆分为多个请求（每请求最多 125 个寄存器/线圈）
- **事务日志**：记录每次请求/响应的 HEX 数据、时间戳、状态
- **轮询模式**：可设置间隔自动轮询读取
- **设备扫描**：自动扫描网络上的 Modbus 从站设备

## 图形界面使用说明

### 1. 打开 MODBUS 窗口

在主界面的工具栏中点击 **MODBUS** 按钮（或按快捷键 `F5`），弹出连接对话框：

- **RTU（串口）模式**：选中 RTU，点击"Connect & Open MODBUS Window"，自动使用当前主串口连接
- **TCP（网络）模式**：选中 TCP，填入设备 IP 地址和端口号（默认 502），点击连接

连接成功后即打开 MODBUS 窗口，包含三个标签页。

### 2. Master 标签 — 读写寄存器

操作面板说明：

| 控件 | 说明 |
|------|------|
| Slave ID | 目标从站地址（1-247） |
| Function | 功能码选择：Read Coils / Read Discrete Inputs / Read Holding Registers / Read Input Registers / Write Single Coil / Write Single Register / Write Multiple Coils / Write Multiple Registers / Mask Write Register / Read Write Multiple Registers |
| Address | 起始地址（十进制） |
| Quantity | 读取/写入数量 |

操作步骤：

1. 选择功能码（如"03 Read Holding Registers"）
2. 输入 Slave ID 和起始地址
3. 点击 **Read** 读取，寄存器列表显示地址、HEX 值、十进制值、二进制
4. 选择写功能码，设置 Write Value，点击 **Write** 写入

**自动轮询：**

勾选 **Auto Poll** 并设置间隔毫秒数，程序将按设定周期自动读取指定范围的数据，适合实时监控从站值。

**事务日志：**

底部 Transaction Log 面板记录所有请求/响应，包含：
- 时间、从站地址、功能码
- 请求和响应的原始 HEX 数据
- 状态（OK 或错误信息）

支持 **CSV / JSON / TXT** 导出和 **Clear** 清空。

### 3. Slave 标签 — 模拟从站设备

可创建虚拟 Modbus 从站，用于测试主站应用或与其他设备联调。

**创建从站：**

1. 点击 **Create Slave** 按钮
2. 弹窗填写：
   - **Slave ID**：从站地址（1-247）
   - **Transport**：TCP（网络监听）或 RTU（串口）
   - **Port**：TCP 模式填端口号，RTU 模式填串口名（如 COM3）
   - **Holding Regs**：保持寄存器数量（默认 256）
3. 点击 **Create** 完成创建

**操作从站：**

创建成功后，从站出现在设备列表中。选中一个从站后可 **Remove Selected** 删除。

通过 HTTP API 或 MCP 工具可读写从站内部的寄存器值。

### 4. Dashboard 标签 — Web 仪表盘

需要安装 [WebView2 Runtime](https://go.microsoft.com/fwlink/p/?LinkId=2124703)。首次切换到 Dashboard 标签时自动加载，显示实时数据面板。

## 功能码速查表

| 功能码 | 功能 | 主站操作 |
|--------|------|----------|
| 01 (0x01) | Read Coils | 读取线圈状态 |
| 02 (0x02) | Read Discrete Inputs | 读取离散输入 |
| 03 (0x03) | Read Holding Registers | 读取保持寄存器 |
| 04 (0x04) | Read Input Registers | 读取输入寄存器 |
| 05 (0x05) | Write Single Coil | 写入单个线圈 |
| 06 (0x06) | Write Single Register | 写入单个寄存器 |
| 15 (0x0F) | Write Multiple Coils | 写入多个线圈 |
| 16 (0x10) | Write Multiple Registers | 写入多个寄存器 |
| 22 (0x16) | Mask Write Register | 掩码写入寄存器 |
| 23 (0x17) | Read Write Multiple Registers | 读写多个寄存器 |

## Modbus 设备扫描

`ModbusScanner` 自动发现 Modbus 网络上的从站设备：

- 按顺序探测指定范围的从站地址（1-247）
- 可配置超时时间
- 支持取消扫描
- 返回在线设备列表（从站地址、首个寄存器值、响应时间）
- MCP 工具：`scan_devices`

## Modbus 从站模拟

`ModbusSlaveService` 支持创建虚拟 Modbus 从站设备：

- 支持 RTU 和 TCP 两种传输模式
- 虚拟设备内存：Coils (1024)、DiscreteInputs (1024)、HoldingRegisters (256)、InputRegisters (256)
- 可动态读写寄存器值
- 用于测试 Modbus 主站应用

## HTTP API

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/slaves` | 获取所有活跃的从站列表 |

**获取从站列表：**

```bash
curl http://127.0.0.1:8899/api/slaves
# → {"success":true,"data":[{"id":"slave_1","slaveId":1,"transportType":"tcp","connectionParam":"15000","isRunning":true}]}
```

## MCP 工具

| Tool | 说明 |
|------|------|
| `read_registers` | 读取 Modbus 寄存器（支持 01-04 功能码） |
| `write_register` | 写入 Modbus 寄存器/线圈（支持 05-06、15-16、22-23 功能码） |
| `slave_create` | 创建虚拟 Modbus 从站设备 |
| `slave_remove` | 移除 Modbus 从站设备 |
| `slave_list` | 列出所有活跃的 Modbus 从站设备 |
| `slave_write` | 向从站设备写入寄存器值 |
| `slave_read` | 从从站设备读取寄存器值 |
| `scan_devices` | 扫描 Modbus 网络上的从站设备 |

## 常见问题

**Q: 打开 MODBUS 窗口后闪退？**
A: 如果是 Dashboard 标签引起的，需安装 WebView2 Runtime。其他情况检查 %LOCALAPPDATA%\ACCcom\crash.log 获取错误详情。

**Q: RTU 模式下没有可选的串口？**
A: 需先在主界面打开串口连接，MODBUS RTU 复用当前串口传输层。

**Q: 切换语言后 MODBUS 窗口文字不变？**
A: 下次打开 MODBUS 窗口即可生效。标签标题、按钮、列头等均跟随主界面语言设置。

## Related

- [协议解析文档](../guide/protocol-parsing.md) — 包含 Modbus RTU .csx 解析器模板
- [高级通信功能](../guide/advanced-comms.md) — 网络桥接（Modbus TCP 传输层）
- [串口操作指南](../guide/serial.md)
