# Changelog

格式约定：`Added / Changed / Fixed` 分组，按提交时间倒序。完整历史见 `git log`（Conventional Commits）。

## Unreleased（实测：构建 0 警告 0 错误，1244 测试全过，Core 行覆盖率 85.6%）

### Added

- MCP 流量窗：ID 列（与 `read_data` 的 sinceId/latestId 对照）、端口 Tag 下拉筛选、暂停/恢复尾随、选中行详情（文本+HEX）、复制选中行、导出过滤结果为 CSV/TXT、状态栏（行数/文件大小/更新时间/暂停态）；MCP 侧 `open_port`/`close_port` 记 SYS 会话日志；单测 +2（SYS 行解析、缺 ID 默认为 0），测试总数 1242 → 1244

### Added

- 单测 +9：`TriggerPathResolver` 穿越拒绝/相对锚定、`HighlightService` 非法正则不抛、`HttpService` 空请求与录制穿越拒绝，测试总数 1233 → 1242（`ACCcom.Core.Tests` 1196 + `ACCcom.McpServer.Tests` 46）

- 新增 `CHANGELOG.md`、`CONTRIBUTING.md`、`.github/ISSUE_TEMPLATE/`（bug/功能模板）
- CI 新增 `Format check`（whitespace/style/analyzers 三项 `--verify-no-changes`）、`Benchmarks` 独立步骤与 `Coverage gate`（Core 行覆盖率 ≥ 80%）
- 单测 +7：`SettingsService` 异常分支（损坏 JSON 的 `LastError`、失败后成功清零、文件锁定读失败、只读文件写失败），覆盖率 71.7% → 87.0%
- 单测 +14：`ParserManager` 生成/解析/指纹/路径解析分支（`GenerateParser` 非法与穿越名、`GenerateParserFromJson`、`GetSchema` 三态、`GetFingerprint`、`TryResolveParserFile`、失败激活的 `LastError`、自定义 dispatcher 热重载），覆盖率 71.3% → 94.0%
- 单测 +8：`SerialServiceBasicsTests`（默认状态、开关/Dispose 生命周期、`SendHex` 透传、坏端口打开失败），`SerialService` 覆盖率 29.1% → 40.9%（剩余需真机串口）

### Changed（主题重设计）

- 7 款主题配色重做：名画概念保留，强调色色相拉开（橙/蓝/金/红/青 + 紫罗兰明暗配对），`Light/Dark` 更名为昼白/墨；状态色提纯（浅色主题的状态蓝改为真蓝，不再与紫色强调色重合）
- 全 63 组前景/背景对比度实测达标（正文 ≥7、次要文本 ≥4.5、按钮字 ≥4.5），危险按钮加深一档补足白字对比度
- 主题入口：标题栏 120px 下拉框改为画廊按钮，弹出 `ThemeGalleryWindow`（实时预览卡 + 一句话说明 + 当前徽章，点击即时生效）；`ThemeOption` 扩展预览色与说明文案，中英双语新增 `Theme.*.Desc`、`GalleryTitle` 等 17 个 key
- 验证：STA 烟雾工程确认 7 个主题字典真实加载、画廊构造/选择/开关正常；真应用启动 12s 存活可响应

### Changed

- 依赖（大版本，已验证）：`ModelContextProtocol` 1.4.0 → 2.2.0、`coverlet.collector` 6.0.0 → 10.0.1、`Microsoft.NET.Test.Sdk` 17.8.0 → 18.10.1、`xunit` 2.5.3 → 2.9.3、`xunit.runner.visualstudio` 2.5.3 → 4.0.0
- 依赖（补丁级）：`Microsoft.Extensions.Caching.Memory` 10.0.9 → 10.0.12、`System.IO.Ports` 10.0.8 → 10.0.12、`Microsoft.Extensions.Hosting` 10.0.7 → 10.0.12、`Microsoft.Web.WebView2` 1.0.4022.49 → 1.0.4191.47
- 格式：全仓 `dotnet format whitespace` 达标（约 180 处缩进/文件尾换行）；`.editorconfig` 移除与现实冲突的 `end_of_line = lf`（仓库实际以 LF 为主、部分历史文件为 CRLF，行尾由 Git `text=auto` 管理）
- `Roslyn C# Scripting` 保持 4.11.0：5.9.0 要求编译器 5.12（CS9057），需等 .NET 10 SDK 迁移后再升

### Fixed

- MCP 流量窗性能与健壮性：`FileSystemWatcher` 200ms 防抖批量追加（单次 `DeferRefresh` + 单次滚动）、越界批量裁剪、日志轮转截断时偏移复位（原逻辑在文件变小后 Seek 越界抛异常走清空偏移分支，现显式处理）、清空改截断而非删除（避免与 MCP 进程追加句柄竞态）
- 安全：`PatternMatcher`/`HighlightService` 正则加 2s 超时并兼容非法 pattern 返回 false；`TriggerPathResolver` 新增 `TryResolve` 拦截 `../` 越界（绝对路径保持兼容）；`SessionRecorder` 纯文件名锚定到 recordings 目录，`HttpService.RecordingStart` 使用解析后路径；`HttpService` MultiPort/Slave 入口补空守卫；`LanguageManager` 仅接受 zh-CN/en-US；`ProtocolTestViewModel` 脚本保存走 `SafePath`；导出 TXT/JSON/CSV 补 try→状态栏
- 稳定：`FrameBuffer.Dispose` 释放 `_parseGate`；`SerialService.Send(hex)` 去掉 4 连 `Replace`，复用已解码字节算 `RawHex`（与 RX 间隔格式对齐）；`SessionRecorder`/`DataFlow.FeedFrameBuffer` 空 catch 改 `Debug` 留痕；`PlotWindow` 缓存 Brush/FontFamily
- 代码收敛：`MacroNaming` 转发 `RuleNaming.Next(prefix, separator)` 单一实现；`ParserGenerator`/`ParserManager` 共用 `ProtocolSchema.JsonOptions`
- 工程：CI 加 `concurrency.cancel-in-progress` + `setup-dotnet.cache`，新增 `global.json` 锁定 8.0.4xx；入门文档补 `Alt+1~9`/`F1`、发布命令与 CI 对齐，集成指南 MCP 工具数 8 → 9 并补 `list_open_ports` 行
- 文档：测试徽章 589 → 1211（`ACCcom.Core.Tests` 1165 + `ACCcom.McpServer.Tests` 46，当时实测）
- 文档：MCP 工具数 8 → 9（补记 `list_open_ports`），同步 README / 集成指南 / 差距分析
- 文档：MCP 配置示例绝对路径改为 `<repo-root>` 占位符；早期 MCP 设计文档加归档注记（代理模式、`--parsers-dir` 已移除）
- 测试：`ModbusRtuTransportTests` 3 个未 `await` 的 `ThrowsAsync` 空断言改为真断言（新版 xunit 分析器 xUnit2021 报出）
- 测试稳定性：`RecentRxTextBuffer` 并发测试加生产者门控（饱和线程池下 `Task.Run` 可能赶不上 300ms 时间盒导致空断言误报）；`MacroManagerRunAsyncTests.TestRunMacroZeroRepeat` 改条件等待（固定 100ms 延时在负载下只能跑出 ≤1 次发送）；吞吐基准与覆盖率采集分离（coverlet 插桩被测热循环会导致必现误报，CI 独立步骤跑基准）
- 文档：测试徽章 1211 → 1233（`ACCcom.Core.Tests` 1187 + `ACCcom.McpServer.Tests` 46）
- 清理：删除根目录残留空目录 `WORK_VSCODEVibe-codingXcomsrcACCcom.Coreparsers/` 与 `src/ACCcom.Core/parsers/`；删除 `tools/serial_monitor.py` tkinter 原型（已被 WPF 取代）；`Agreement/` 下乱码目录重命名为 `迪瑞`（本地忽略目录）

## 2026-06 ~ 2026-09（按领域汇总，共 247 commits）

### Added

- 串口：多端口并发、断线重连（含设备等待）、设备插拔检测、自动波特率探测、TCP/UDP 桥接、虚拟串口模拟
- 协议解析：Roslyn C# Script 引擎、热加载、自动解析器匹配、可视化编辑器、测试运行器、协议→解析器生成流程
- Modbus：RTU/TCP/ASCII 主站、10 种功能码、设备扫描、从站模拟、轮询、日志导出
- 自动化：会话录制回放、多步骤宏（含 WaitFor/条件门控）、触发器规则（pattern 匹配动作）
- 数据：Channel+RingBuffer 缓冲、实时统计、TXT/JSON/CSV/PCAP 导出、包过滤表达式、高亮规则、数据对比/差异分析、实时波形绘图
- 界面：7 款主题、中英文运行时切换、书签、配置预设、快捷指令、发送历史、状态栏、F1 快捷键总览
- AI：独立 MCP stdio 服务器（9 个串口工具）、内嵌 HTTP REST API（`127.0.0.1:8899`，可选 Token）、WebSocket 推送、MCP 流量監控窗口
- 测试体系：1233 个单测，覆盖串口/解析/Modbus/宏/触发器/导出/HTTP/MCP 工具

### Changed

- 架构拆分：业务逻辑下沉 `ACCcom.Core`（net8.0，无 WPF 依赖），MCP Server 独立进程直连串口
- 发送字节统计统一到 `HexHelper.CountSendBytes`；HEX 解析与校验对齐
- 清理死命令、过期文档与 `bin/obj` 误提交风险（`.gitignore` 覆盖构建产物与运行时文件）

### Fixed

- 近期（9 月）：plot 非有限点一致拒绝、replay 延迟溢出钳制与 NaN 速度拒绝、空缓冲查询结果隔离、HEX 空白载荷严格字节数、Modbus 空 PDU 非法数据契约等（详见 `git log`）
