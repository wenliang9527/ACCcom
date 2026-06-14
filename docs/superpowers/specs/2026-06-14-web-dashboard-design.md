# Web Dashboard 设计文档

## 概述

在 WPF `ModbusWindow` 新增 Dashboard 标签页，通过内嵌 WebView2 显示实时 HTML 仪表盘。仪表盘利用现有 EmbedIO HTTP 服务器（`HttpService`，端口 8899）和 WebSocket 端点提供数据。

## 架构

```
WPF ModbusWindow
  └─ TabControl
       ├─ Master (现有)
       ├─ Slave (现有)
       └─ Dashboard (新增: WebView2 ─── http://localhost:8899/dashboard/)
                                              │
                 ┌────────────────────────────┼────────────────────────────┐
                 │                            │                            │
            /api/status                 /api/slaves                    /ws
          (已有: 串口状态)          (新增: 从机+寄存器)          (已有: 实时数据推送)
```

## 组件说明

### 1. Dashboard HTML 页面

**文件：** `src/ACCcom.Core/wwwroot/dashboard.html`

单 HTML 文件，内嵌 CSS 和 JS。三个面板使用 flexbox 布局。

#### 面板布局

```
┌─────────────────────────────────────────────────────────┐
│ ┌─────────────────────────────────────────────────────┐ │
│ │ Status Bar: COM3 | 115200 | RX: 1024 | TX: 512    │ │
│ └─────────────────────────────────────────────────────┘ │
│ ┌──────────────────────┐ ┌────────────────────────────┐ │
│ │ Real-Time Data Stream│ │ MODBUS Slaves              │ │
│ │ (可折叠)              │ │                             │ │
│ │                      │ │ Slave 0x01 | TCP:15000     │ │
│ │ [RX] AA 55 03 01...  │ │ ┌─────┬───────┬──────────┐│ │
│ │ [TX] AA 55 03 02...  │ │ │Addr │ Value │ Hex      ││ │
│ │ [RX] AA 55 03 03...  │ │ │ 0   │ 4660  │ 0x1234   ││ │
│ │                      │ │ │ 1   │ 100   │ 0x0064   ││ │
│ │                      │ │ │...  │       │          ││ │
│ └──────────────────────┘ └────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

#### 状态栏（顶部）
- 串口：端口名 + 波特率
- RX/TX 计数
- 活跃连接数
- 颜色编码：连接时绿色，断开时红色
- 轮询频率：2 秒（通过 `/api/status`）

#### 实时数据流（左侧）
- WebSocket `/ws` 实时接收 RX/TX 条目
- 显示最近 100 条，自动滚动到最新
- 每条显示：方向标签（RX=蓝/TX=橙）、时间、十六进制数据
- 默认收起（可折叠），点击展开
- 对 RX 数据自动调用 `/api/parser/parse-raw` 显示解析字段

#### MODBUS Slaves 面板（右侧）
- 通过 `/api/slaves` 获取数据，1 秒轮询
- 顶部：从机列表（Slave ID + Transport）
- 底部：选中从机的前 32 个 HoldingRegisters 表格
- 寄存器表格列：地址、值（DEC）、值（HEX）

#### 数据源

| 数据 | 方式 | 端点 |
|------|------|------|
| 实时数据 | WebSocket 推送 | `/ws` |
| 串口状态 | 轮询 2s | `GET /api/status` |
| 从机+寄存器 | 轮询 1s | `GET /api/slaves`（新增） |
| 解析字段 | 按需调用 | `POST /api/parser/parse-raw`（已有） |

### 2. 新增 API 端点

**文件：** `src/ACCcom.Core/Services/HttpService.cs`

#### `GET /api/slaves`

返回所有活跃从机及其寄存器数据：

```json
{
  "slaves": [
    { "id": "slave_1", "slaveId": 1, "transportType": "tcp", "connectionParam": "15000", "isRunning": true }
  ],
  "registers": {
    "slave_1": [
      { "address": 0, "value": 4660, "hex": "0x1234" },
      { "address": 1, "value": 100, "hex": "0x0064" }
    ]
  }
}
```

新增 `HttpService` 构造函数参数：接受可选的 `ModbusSlaveService`。

#### `GET /dashboard/`

返回 `dashboard.html` 文件内容（从嵌入式资源或 wwwroot 磁盘文件读取）。

### 3. HttpService 修改

**文件：** `src/ACCcom.Core/Services/HttpService.cs`

- 构造函数增加可选 `ModbusSlaveService?` 参数
- 使用 `WithStaticFolder` 或手动路由提供 `/dashboard/` → `dashboard.html`
- 新增 `/api/slaves` 路由

### 4. WPF Dashboard Tab

**文件：** `src/ACCcom/ModbusWindow.xaml`

新增一个 TabItem，包含 `WebView2` 控件，Source 指向 `http://localhost:8899/dashboard/`。

需要 `Microsoft.Web.WebView2` NuGet 包。

**后备方案：** 如果 WebView2 运行时未安装，回退到文本提示（"请在浏览器中打开 http://localhost:8899/dashboard/"）。

## 文件清单

### 新建
- `src/ACCcom.Core/wwwroot/dashboard.html` — 仪表盘页面（~250 行）

### 修改
- `src/ACCcom.Core/Services/HttpService.cs` — 添加 `/dashboard/` 和 `/api/slaves` 路由
- `src/ACCcom/ModbusWindow.xaml` — 添加 Dashboard Tab 和 WebView2
- `src/ACCcom.ModbusWindow.xaml.cs` — 可选：WebView2 生命周期管理

## 现有代码影响

- **HttpService.cs** — 构造函数增加可选参数，新增 2 个路由，非破坏性
- **ModbusWindow.xaml** — 纯新增 Tab，不影响现有 Master/Slave 功能
- **ModbusSlaveService** — 无变化（通过新 API 端点调用）
- **SerialController** — 无变化
- **测试** — 无需修改（无新业务逻辑）

## 实现顺序

1. 创建 `dashboard.html`
2. 修改 `HttpService.cs`（新增路由）
3. 修改 `ModbusWindow.xaml`（WebView2 Tab）
4. 构建验证
