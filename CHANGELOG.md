# Changelog

格式约定：`Added / Changed / Fixed` 分组，按提交时间倒序。完整历史见 `git log`（Conventional Commits）。

## Unreleased（实测：构建 0 警告 0 错误，1372 测试全过）

### Changed

- 交互手感（R9 轮）：移除 6 处实时列表的 `IsDeferredScrollingEnabled`（Modbus 寄存器/事务日志、虚拟串口流量、DataPanel 合并/RX/TX）——拖动滚动条时内容不再冻结，实时日志可边拖边看；发送框 HEX 校验失败改纯颜色提示（原 1→1.5px 边框变化会让整条发送栏布局跳动）；状态栏 6 个单位标签（RX/TX/Err/Up/Buf/REC）本地化为 `StatusBar.*Label` 语言键——**Run.Text 默认 TwoWay 绑定**，对只读索引器必须在 Binding 内显式 `Mode=OneWay`，否则初始化即抛异常（crash.log 抓到 3 次后才定位）
- MinWidth 960 与固定宽度下拉框经评估暂不改动：需真机视觉验证收益，避免盲改布局

### Fixed

- **主题切换误删 Tokens 字典（R8 轮，R7 回归）**：`ApplyTheme` 按路径含 `Themes/` 清理合并字典——R7 的 `Themes/Tokens.xaml` 同样命中，应用主题时 token 字典被整个移除，之后解析的窗口（流量窗）所有 `StaticResource FontSize*` 抛 `XamlParseException`，`--open-mcp-traffic` 启动即崩、主窗口存活掩盖（crash.log 实证）；清理逻辑豁免 Tokens。冒烟方法升级为「启动流量窗 + crash.log 前后差分」而非仅进程存活——进程存活检查抓不到次级窗口崩溃

### Changed

- 界面结构化收尾（R8 轮）：`ChromeTitleBar` 新增 `CenterContent`（标题与窗口按钮之间居中槽）与 `ShowAccentDot`（品牌光点，经内联 XAML 片段保留 DynamicResource 主题跟随）；MainWindow 最后一个自绘标题栏完成迁移（连接状态徽章入 CenterContent，3 个 Click 处理器删除），全部 23 个窗口统一走共享控件；剩余文本字形全部 MDL2 化（4 个 ExtraButtons 关闭钮 ✕ → ChromeClose、多端口 "+" → Add 图标）

### Added

- MCP `open_port` 参数预校验（R6 轮）：`baudRate > 0`、`dataBits 5-8`、`stopBits 0/1/2`、`parity 0/1/2` 越界直接报新稳定错误码 `INVALID_CONFIG`（此前落进 SerialPort 深处的 ArgumentException 变成含混的 OPEN_FAILED）；工具描述同步数值范围；测试数 1372（`ACCcom.Core.Tests` 1267 + `ACCcom.McpServer.Tests` 105，+6：5 组越界参数 + 界内 7E2 组合仍正常打开的防过杀守护）
- MCP 流量窗 tool 下拉过滤：Tag 筛选旁新增工具筛选框（全部工具/rx/send/send_and_wait/open_port/close_port），随日志自动补条目、与方向/tag/搜索过滤正交，视图状态随窗口持久化（`AppSettings.McpTrafficToolFilter`）
- MCP 流量窗 Len 列：每条交换的解码字节数（HEX 去空格折算，SYS 行留空），一眼看出包大小
- 列宽持久化改列名键：`TrafficColumnWidthStore` 与 `AppSettings.McpTrafficColumnWidthsByName` 以列名（Id/Time/Direction/Tool/Tag/Length/Payload）为键——原索引键方案在插入/重排列时把所有已存宽度静默错映射到别的列，本次新增 Len 列正是会触发的场景；未知列名回退默认宽

### Changed

- README/CONTRIBUTING 测试徽章同步 1372；integration.md 错误码清单补 `INVALID_CONFIG`

- 界面结构化（R5a 轮）：新增 `ChromeTitleBar` 共享标题栏控件——22 个窗口此前各自复制粘贴同一段 32px 自定义标题栏（Border + 三按钮 + 三个 Click 处理器 + `SetupTitleBar` 调用，共约 66 处重复处理器、22 处重复 XAML 块）；控件以 Border 子类代码构建（标题本地化绑定、`ShowMinMax`/`ShowClose`/`ExtraButtons` 三个适配属性），拖拽/双击最大化仍走 `WindowHelper.SetupTitleBar`，应用启动冒烟（`--open-mcp-traffic` 打开流量窗）验证存活。窗口按钮字形从文本字符（─ □ ✕）统一为 Segoe MDL2 矢量字形（ChromeMinimize/ChromeMaximize/ChromeRestore/ChromeClose），最大化钮随窗口状态在最大化/还原字形间切换；McpTrafficWindow 的 Clear 与三个对话框的 `DialogResult=false` 语义经 `ExtraButtons` 精确保留（`ShowClose="False"`），PromptDialog 代码设标题改为控件 `Title` 属性；MainWindow 标题栏含居中状态胶囊，保持自绘不入本轮
- 主题字典去重：7 主题本已定义 `OnAccentBrush`，R4 轮以 `#FFFFFF` 硬编码新增了一份同名键——运行时 `XamlParseException: Item has already been added`（启动即崩，crash.log 实证）；已删除脚本插入的重复项，保留主题原有的 `{StaticResource OnAccent}` 调校值，`App.xaml` 的开口态前景随之获得主题化对比色
- 侧栏单源化（R5b 轮）：快速发送栏的三处 `Visibility` 绑定（分栏条/侧栏本体/折叠 rail）此前写的是 `{Binding ShowQuickSendSidebar, RelativeSource={RelativeSource AncestorType=Window}}`——窗口对象上根本不存在该属性，绑定静默失败、从未生效（实际工作的一直是代码后台 `ApplySidebarVisibility` 的命令式赋值，双机制各自为政）。绑定修正为指向窗口 `DataContext.ShowQuickSendSidebar` 后成为唯一可见性来源；代码侧收敛为 `ApplySidebarWidth`，只负责 GridLength 列宽（无干净的绑定路径）：收起前记住拖拽宽度、展开时恢复并 clamp 180–420

### Fixed

- 界面快赢包（R4 轮）：状态栏运行时长双渲染修复（`Text` 绑定与 `Run` 同时存在曾把时长显示两遍「12:34Up 12:34」）；Light/Dark 主题 `DividerBrush` 与 `BorderColor` 同值 + 自定义分隔条 0.25 透明度导致分隔条几乎不可见——分隔线独立成 token（Light #CFD5E0 / Dark #3A3A44），主窗口与 DataPanel 两处分隔条模板改全不透明分隔线色 + hover 强调色
- 交互态补全：`ToolToggle`/`MiniToggle`/`TitleBarButton`/`TitleBarCloseButton` 补 IsPressed 与禁用（0.4 透明度）视觉态；快速发送栏 rail 补 hover 反馈（此前是无反馈的静默点击区）
- 硬编码色 token 化：关闭钮 `#E81123`/`#FFFFFF`、开口态前景 `#FFFFFFFF`、PaintedCard 高光渐变（主题不变的白雾）分别落为 7 主题新增的 `DangerStrongBrush`/`OnDangerBrush`/`OnAccentBrush`/`CardVarnishBrush`（暗色主题高光自动减淡为 0x12/0x22 档）
- DataPanel 三个搜索框加水印（复用 `Tip.SearchAll/Rx/Tx` 键，与流量窗占位一致）；主窗口恢复位置/尺寸 clamp 到当前工作区（换显示器/分辨率后不再开到屏幕外）

### Added

- MCP 流量窗体验与性能（R3 轮）：搜索 150ms 防抖（清空立即生效，打字不再每键全量 Refresh）；搜索改为字段无关匹配（Text/Hex 独立于 HEX/ASCII 显示模式，切换不再改变搜索结果）；新行读取+解析移出 UI 线程（worker 线程解析、Dispatcher 回传追加，在途互斥 + 完成后自动补跑，突发洪流不再卡 UI）；溢出超 512 行的单次批量裁剪代替逐行 `RemoveAt(0)`；SYS（open/close）行改中性配色（不再冒充 RX 绿）；tool 记录 `id=0` 显示「—」（详情元信息同步）；payload 列自动伸展填充剩余宽度（其余列保持用户拖宽）；视图状态持久化（HEX 模式/方向/tag/搜索/follow-tail 五个 `AppSettings` 新字段）；导出新增 JSON（无损数组格式）；详情框 MaxHeight 44 → 120；删除死代码 `AppendLine`/`NoteTag`/`UpdateRowCount`
- `McpTrafficLog` 清空竞态加固：flush tick 检测外部截断（GUI 清空对 append 句柄 `SetLength(0)`）并重开新文件——此前按旧偏移写入会留下稀疏洞（撕裂 JSONL）且 `_lineCount` 失账导致提前轮转；清空时缓冲中的历史行随之丢弃（最多损失一个 flush 周期的写入）
- 单测 +1：`McpTrafficLog` 外部截断重开契约（无稀疏洞、清空后历史不复活）；另将 `RxHotPathBenchmarkTests.McpTrafficLog_Record` 基准改三轮取最优（26ms 窗口单轮易受调度/Defender 扫描噪声影响，实测复现连续 195k/s < 200k 阈值的假阳性），测试总数 1364（`ACCcom.Core.Tests` 1265 + `ACCcom.McpServer.Tests` 99，以实测为准）

### Added

- MCP 响应一致性（R2 轮）：`open_port` 已开口响应与新开口统一 schema（补 `port`/`baudRate`/`dataBits`，取自实际生效配置 `ISerialService.ActiveConfig`（新增），不再回显本次调用的参数）；`send_and_wait` 响应补 `byteLength`（与 `send` 同契约：HEX 严格解码计数、文本 UTF-8 计数）；`read_data` 服务端输出预算——省略 `maxLength` 时默认每条 2000 字符上限（满环 text+hex 双列否则可向模型吐几十 MB），显式值放宽至 65536 封顶
- MCP `open_port` 异步化：`SerialService` 重试等待从 `Thread.Sleep` 改为 `Task.Delay`（新增 `ISerialService.OpenAsync` 默认接口方法，轻量实现零成本继承；`MultiPortService.OpenPortAsync` 异步孪生，接线/快速路径/双检注册提取共享），失败重试不再占住线程池线程约 1s
- 单测 +5：已开口形状（默认会话/多端口 tag，参数不一致时报告实际配置）、`send_and_wait` 超时也带 `byteLength`、默认上限截断与 65536 封顶、`MultiPortService.OpenPortAsync` 注册与幂等，测试总数 1357 → 1362（`ACCcom.Core.Tests` 1263 + `ACCcom.McpServer.Tests` 99）

### Added

- MCP 健壮性（R1 轮）：`ErrorCodes` 新增 `INVALID_PATTERN` 与 `INTERNAL`；`read_data`/`wait_for_response`/`wait_for_quiet`/`clear_buffer` 对未打开的 tag 立即报 `PORT_NOT_OPEN`（此前静默返回空数据、`wait_*` 阻塞整个超时、`clear_buffer` 为拼错的 tag 永久分配孤儿 buffer）；`close_port` 对未开端口/未知 tag 诚实报 `PORT_NOT_OPEN`（此前 `SerialService.Close()` 恒真、多端口未知 tag 返回 true，假成功误导 agent）；`wait_for_response`/`send_and_wait` 前置校验 `matchMode`（未知值此前静默按 contains 降级）与 regex 可编译性（非法 regex 此前表现为超时）；工具体统一 `ToolContext.Guard` 包裹，未预期异常以 `INTERNAL` 信封返回而非原始异常文本
- stdio 日志隔离：`Program.cs` 改用 `Host.CreateEmptyApplicationBuilder` + Console 日志仅 stderr——实测默认 builder 把 24 行 `info:` 托管日志与 JSON-RPC 响应交错写进 stdout（宽客户端容忍、严格客户端解析失败），修复后探针验证 stdout 仅 2 行 JSON、24 行日志全部落到 stderr
- 单测 +11：`INTERNAL` 信封（throwing fake 依赖）、未知 tag 五工具报错且不分配 buffer、未知 matchMode/非法 regex 报 `INVALID_PATTERN`、大小写混合模式接受、close 诚实化契约更新，测试总数 1346 → 1357（`ACCcom.Core.Tests` 1263 + `ACCcom.McpServer.Tests` 94）

### Changed

- CI 基准缺口修复：主测试运行按名字排除 `RxHotPathBenchmarkTests`，但 Benchmarks 步骤只单独跑了 Core 的基准——`McpRxHotPathBenchmarkTests` 在 CI 从未执行；现补上 MCP 基准独立运行。本地偶发的 `ToolContext_ReceiveChain_SustainsHighThroughput` 失败即它与 Core 1263 测试并行时的 CPU 竞争（实测 62k/s < 100k 阈值），按 CI 口径（排除过滤 + 基准单独跑）后全绿

### Added

- MCP 节 token 与标准化（第三轮）：`read_data` 新增 `fields` 列选择（`id,timestamp,direction,portTag,text,hex,truncated` 子集，默认全量，`fields=text` 砍掉最大的 hex 列，未知列报 `INVALID_FIELDS`）；响应稀疏化（空 `portTag`、`truncated:false`、空 `text`/`hex` 一律省略，`data` 内回显的空 `tag` 全工具统一省略——`ToolContext.JsonOpts` 开 `WhenWritingNull`）；时间戳全工具统一 ISO-8601 毫秒精度（不再 7 位小数）；`send`/`send_and_wait` 不再回显发送内容；`wait_for_response`/`send_and_wait`/`wait_for_quiet` 响应补 `latestId`，任何读/等操作后可用 `read_data(sinceId=latestId)` 无缝续游标；典型 100 条 ASCII 轮询响应约减半（全字段 -14%、`fields=text` 约 -55%）
- MCP 错误结构化：失败信封改 `{"success":false,"error":{"code","message"}}`，新增 `ErrorCodes` 稳定错误码（PORT_NOT_OPEN / INVALID_HEX / EMPTY_DATA 等 9 个），agent 按 code 机器分支、message 面向人类
- 工具 schema 瘦身：工具级描述去重下沉到参数级（工具级 3424 → 1226 字符，总 schema 5661 → 3624 字符，-36%；对 inline 工具宿主每次 API 请求省约 550 token）；新增 `ToolSchemaBudgetTests` 预算守卫（工具级 <1600、参数级 <3200、工具数恒为 10）
- 单测 +16：fields 三态/稀疏省略/时间戳格式/结构化错误码/去回显/三工具 latestId/游标交接无空隙/空 tag 回显全工具统一，测试总数 1330 → 1346（`ACCcom.Core.Tests` 1263 + `ACCcom.McpServer.Tests` 83）

### Changed

- MCP `read_data` 长轮询：新增 `waitMs`（clamp 0–60000；游标耗尽时事件驱动挂起，数据到达即返回、否则最多等 waitMs，tail 模式忽略该参数），`DataBufferService.WaitEntriesSinceAsync` 实现（注册与复查同 `_lock` 无丢唤醒，`RingAdd` 内释放到达信号，RunContinuationsAsynchronously 防续延同步执行）——agent 轮询从「定时往返」变「事件驱动」
- MCP 读路径 source-gen 序列化：新增 `McpJson`（source-gen DTO + `Lean(entry, maxLength)` 投影），`read_data`/`wait_for_response`/`send_and_wait` 响应均走 lean（省 HighlightColor/IsSearchMatch/Fields 等 UI 字段，每条省约 56 字符 ≈ 25% 响应体积）；实测 read_data 每次 238µs → 130–180µs（反射对照 267µs）；`ACCcom.McpServer` 增加 InternalsVisibleTo 测试缝
- 单测 +12：waitMs 到达即返/超时/方向过滤游标推进/尾随忽略、`WaitEntriesSinceAsync` 立即返回/到达唤醒/超时游标不动/零等待短路/注册竞态 50 连/并发 10 等待者扇出，测试总数 1318 → 1330（`ACCcom.Core.Tests` 1263 + `ACCcom.McpServer.Tests` 67，含 3 个吞吐基准）；新增 `read_data` 满环吞吐基准（阈值 2000 ops/s，防数量级回归）

### Added

- MCP `wait_for_quiet` 工具：等待串口静默 quietMs 毫秒（轮询到达序列游标，无调用方忙等），推荐流式响应收尾后接 `read_data tail` 取全文；MCP 工具数 9 → 10，同步 README / 集成指南 / 架构 / 两处归档注记
- `read_data` 新增 `tail`（免游标取最新 N 条，latestId 与 sinceId 增量轮询无缝衔接）与 `maxLength`（超长 text/hex 截断并标 truncated）；响应改为 lean 投影，不再携带 HighlightColor / IsSearchMatch / Fields 等 UI 字段
- MCP 流量日志可注入：`ToolContext.TrafficLog` 可替换（默认 `McpTrafficLog.Shared`），测试工厂注册临时文件实例，单测不再污染真实 mcp-traffic.jsonl；新增吞吐基准 3 个（流量行写入、RX 接收链路、满环 wait 注册），阈值以实测定档
- 单测 +34：MCP 性能/去重/泄漏、读路径 tail/quiet/lean、多端口慢服务锁收窄，测试总数 1284 → 1318（`ACCcom.Core.Tests` 1257 + `ACCcom.McpServer.Tests` 61）

### Changed

- MCP 热路径性能：`McpTrafficLog` 关闭逐行 AutoFlush，改缓冲写 + 100ms 定时 flush + 延迟轮转 + source-gen JSON（实测流量行 144k → 275k 行/s，RX 接收链路 68k → 164k 条/s；GUI 流量窗为 FileSystemWatcher 防抖尾随，容忍 ≤100ms 延迟）；TX 不再被接收 handler 双写（每次发送只记一行，带真实工具名与 portTag）
- `DataBufferService.WaitForMatchAsync` 模式扫描移出 `_lock`（快照与注册仍在同一 `_lock` 临界区，保证无丢唤醒；regex 扫满环不再阻塞 AddEntry 与串口 RX 线程）
- `MultiPortService` 锁收窄：Open（双检）/Send/Close 全部移出 `_lock`，慢端口打开、阻塞写、慢关都不再阻塞其它端口的查询与收发；`ToolContext.Buffers` 改 ConcurrentDictionary

### Fixed

- `DataBufferService.LastSeq` 差一：RingAdd 赋 `++_nextSeq`（最后 seq 即 `_nextSeq`），属性却返回 `_nextSeq - 1`——change-detection 只看单调性故从未暴露，公开给 wait 工具的 `latestId` 游标后返回值恒小 1（会重读一条）；连同上一轮 `WaitEntriesSinceAsync` 重查条件的同类差一一起修正
- `WaitEntriesSinceAsync` 注册重查差一（`_nextSeq - 1 > id` 应为 `_nextSeq > id`：RingAdd 赋 `++_nextSeq`，最后 seq 即 `_nextSeq` 本身）——唤醒后重判总失败、退化为整段 waitMs 超时（修复前该组测试 112s，修复后 1s）
- MCP 等待队列泄漏：命中与超时的 waiter 显式注销（此前超时的 waiter 滞留 `_waiters` 直到下一条数据才被扫出，空闲端口轮询会持续累积）；`OpenPort` 打开异常时释放已创建的 service（原实现持锁抛出并泄漏）
- GUI 统计与会话修复（1244 → 1284）：统计窗数据单源化并移除重复的 TxThroughputWindow、缓冲清空双向同步、会话记录 fields 归位、hex 空格展示、侧栏开关持久化与 Ctrl+H 分栏、logger 接线、RefreshExisting 时序；`ModbusRtuTransport`/`ModbusService`/`SerialServiceIntegration` 3 组断言对齐当前 hex 间距格式

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
