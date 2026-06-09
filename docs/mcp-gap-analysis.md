## ACCCOM MCP 服务器差距分析

> 上次更新: 2026-06-09

### 项目现状

ACCCOM 是一个功能较完整的 WPF 串口调试工具，已具备：串口收发（含自动重连）、HTTP REST API（EmbedIO :8899）、C# 脚本协议解析器（Roslyn）、日志系统、循环发送、MVVM 架构。

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

SerialService 的 Open/Close/Send 已改为返回 bool。

---

### 三、MCP 阶段的待办事项

#### 待完成：项目拆分

- [ ] 创建 `ACCcom.Core` 类库（net8.0，无 WPF 依赖）
- [ ] 移动 Models/ 和 Services/ 到 Core
- [ ] 解耦 ParserManager 的 SynchronizationContext
- [ ] ACCcom.Wpf 引用 Core，删除已移出的源文件
- [ ] 编译验证：WPF 功能不变

#### 待完成：MCP 服务器

- [ ] 创建 `ACCcom.McpServer` 控制台项目
- [ ] 添加 `ModelContextProtocol` NuGet 包
- [ ] 实现 stdio 传输
- [ ] 注册串口管理工具（list_ports, get_status, open_port, close_port）
- [ ] 注册数据收发工具（send, read_data, wait_for_response, clear_buffer）
- [ ] 注册解析器管理工具（list_parsers, read_parser, write_parser, activate_parser, parse_raw）
- [ ] 注册 MCP Resources（parsers 列表、解析器源码）
- [ ] AI 客户端中注册并验证连接

#### 待完成：协议解析闭环

- [ ] 定义 .csx 脚本规范文档（AI 生成标准）
- [ ] 实现 `write_parser` 工具（AI 写脚本到 parsers/ 目录）
- [ ] 实现 `read_parser` 工具（AI 读取已有脚本）
- [ ] 实现 `parse_raw` 工具（离线解析，不经过串口）
- [ ] 编写 MCP Prompt 模板（引导 AI 完成"读协议→写脚本→验证→激活"流程）

---

### 四、HTTP API 与 MCP 的关系

| 层 | 用途 | 状态 |
|---|---|---|
| HTTP API (EmbedIO) | GUI 调试、curl/PowerShell 测试、向后兼容 | ✅ 已完成，保留在 ACCcom.Wpf |
| MCP stdio | AI 客户端调用 | 🔲 待实现，在 ACCcom.McpServer |

两者共享 ACCcom.Core 业务逻辑，只是传输层不同。
