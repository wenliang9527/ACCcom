## ACCCOM MCP 服务器差距分析

> 上次更新: 2026-06-09（已转为代理模式）

### 项目现状

ACCCOM 是一个功能较完整的 WPF 串口调试工具，已具备：串口收发（含自动重连）、HTTP REST API（EmbedIO :8899）、C# 脚本协议解析器（Roslyn）、日志系统、循环发送、MVVM 架构。

**运行模式**：默认使用 **代理模式**（`launch_acccom.ps1`），启动时自动拉起 WPF 桌面端，MCP Server 通过 WPF 的 HTTP API 操作串口，AI 操作与桌面 GUI 实时同步。

---

### 一、架构决策（已确定）

**选择：项目拆分方案（非 HTTP 桥接）**

将业务逻辑提取到 `ACCcom.Core` 共享库，新建 `ACCcom.McpServer` 控制台项目直接引用 Core + MCP C# SDK。

理由：桥接方案需要 ACCCOM GUI 在运行才能工作，而拆分方案让 MCP Server 可以独立运行——用户不需要同时打开 WPF 窗口就能让 AI 控制串口。

详细设计见 `docs/design/2026-06-09-acccom-mcp-server-design.md`。

---

### 二、HTTP API 补全状态（已完成）

所有 P0/P1/P2 端点已在上一轮工作中实现，编译通过 0 错误 0 警告：

| 端点 | 状态 | 说明 |
|------|------|------|
| `GET /api/ports` | ✅ 已有 | 列出可用串口 |
| `GET /api/status` | ✅ 新增 | 连接状态 + 配置 + 计数 |
| `POST /api/port/open` | ✅ 新增 | 打开串口（JSON 参数） |
| `POST /api/port/close` | ✅ 新增 | 关闭串口 |
| `POST /api/send` | ✅ 改进 | 支持 JSON body + 统一响应 |
| `GET /api/data` | ✅ 改进 | 新增 limit/direction 过滤 |
| `POST /api/wait-for` | ✅ 新增 | 阻塞等待匹配数据 |
| `POST /api/clear` | ✅ 新增 | 清空缓冲区 |
| `GET /api/parsers` | ✅ 新增 | 列出解析器 |
| `POST /api/parser/activate` | ✅ 新增 | 激活/停用解析器 |
| `GET /api/health` | ✅ 改进 | 统一响应格式 |

所有端点统一返回 `{ success, error?, data? }` 格式。

> **注意**：JSON 请求属性名采用 PascalCase（首字母大写），与 C# 模型属性名一致。例如 `{"Port":"COM3","BaudRate":115200}` 而非 `{"port":"COM3","baudRate":115200}`。

SerialService 的 Open/Close/Send 已改为返回 bool。

---

### 三、MCP 阶段的待办事项

#### 已完成

- [x] 创建 `ACCcom.Core` 类库（net8.0，无 WPF 依赖）
- [x] 移动 Models/ 和 Services/ 到 Core
- [x] 解耦 ParserManager 的 SynchronizationContext
- [x] ACCcom.Wpf 引用 Core，删除已移出的源文件
- [x] 编译验证：WPF 功能不变
- [x] 创建 `ACCcom.McpServer` 控制台项目
- [x] 添加 `ModelContextProtocol` NuGet 包
- [x] 实现 stdio 传输
- [x] 注册 13 个 MCP 工具
- [x] 实现 `write_parser` / `read_parser` / `parse_raw` 工具
- [x] 创建 `launch_acccom.ps1` 启动脚本（自动拉起 WPF + MCP Server 代理模式）
- [x] OpenCode 配置已更新，默认代理模式开箱即用

---

### 四、HTTP API 与 MCP 的关系

| 层 | 用途 | 状态 |
|---|---|---|
| HTTP API (EmbedIO) | GUI 调试、curl/PowerShell 测试、代理模式的后端 | ✅ 已完成，保留在 ACCcom.Wpf |
| MCP stdio | AI 客户端调用 | ✅ 已完成，默认代理模式 |

两者共享 ACCcom.Core 业务逻辑。代理模式下 MCP 通过 HTTP API 操作串口，AI 操作与桌面 GUI 实时同步。
