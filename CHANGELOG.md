# Changelog

格式约定：`Added / Changed / Fixed` 分组，按提交时间倒序。完整历史见 `git log`（Conventional Commits）。

## Unreleased（实测：构建 0 警告 0 错误，CI 口径主套件 1561 全过 + 基准 11 单列全过；全面审计见 docs/audit-2026-10-24.md）

### Added

- **MCP `open_port` 支持 TCP 串口端点（无线 DAP UART 桥）**（`feat(core,mcp)`，2026-10-27）：`port` 参数除 `COM3` 外接受 `tcp://host:port`（规范）与裸 `host:port`（便利形式，Windows 串口名不含冒号、无歧义），响应统一回显归一化端点——agent 可直连 windowsair 无线 DAP 的 UART-TCP 桥（TCP 1234），与 SWD/usbip 烧录通路并行，无线烧录 + 无线看日志同链路成立
  - **TcpSerialService**（新 `ISerialService` 实现，仿 VirtualSerialService 先例）：开连接后**把 baudRate 作为首包握手发出**（纯 ASCII 数字、无结尾、Open 返回前、单次写）——对 windowsair 桥同时完成「切桥侧 UART 波特率」与「占死首包换速指令窗口」两件事（固件只认连接后第一包 2-7 位纯数字，且无论是否解析成功窗口即关闭；不占死则 agent 首条裸数字载荷会被吞且波特率被改坏），握手经正常 TX 路径记录进缓冲/traffic_log；断线自动重连**必重握手**（幂等治愈固件中途重启回默认波特率）；`set_baud` 走**透明重连+重握手**（换速窗口只在连接后首包存在，中途发数字只会变垃圾直达目标板），tag/缓冲保持不丢——对比 close+reopen 会删 tag 缓冲，波特率探测不丢已收证据；波特率 >1999999 提前 `INVALID_CONFIG`（固件上限）
  - **EndpointSerialService 分发器**：Open 时按 `PortName` 懒建 Tcp/Serial 内层实现——分发必须做在 service 内部而非工厂，因为默认会话是 DI 注入的裸 `ISerialService`、MultiPortService 工厂不收 config；Program.cs 两处注册换分发器后 read_data/wait_*/clear_buffer/traffic_log 全部零改动受益
  - **已知语义**（integration.md 新增「TCP 端点」节）：单客户端（新连接被拒是固件固有行为）；set_baud 重连间隙丢在途字节；非 windowsair 裸 TCP 端点（USR 类网串服务器）会收到 6 字节握手——排障清单项；dtr/rts/校验位对 TCP 无意义被忽略
  - **测试 1507 → 1558（+51）**（徽章原记 1496 在 81c0fa6 后已过期）：Core +42（传输契约/端点解析 24 用例/分发与 MultiPort 集成/socket 释放）/ McpServer +6（open_port 双形式回显/超上限/OPEN_FAILED/echo 端到端/带 tag 会话）；TCP 测试类入 `DisableParallelization` 独占集合——48 个测试的活 socket + 握手时机进并行池会让套件里贴边的时敏测试轮换挂（干净基线 ×2 全绿对照定性），独占后 Core 1419 稳定全绿
  - **socket 生命周期加固（异常退出不占死单客户端桥）**：全路径审计后补两缺口——① MCP 宿主 stdio EOF 时 `RunAsync` 返回即进程退出、容器从未 Dispose，现 `finally { app.Dispose() }`（此 host 的 IHost 无 DisposeAsync，同步等价）让串口单例走排空+FIN 而非裸靠内核；② 新增 `~TcpSerialService` 终结器兜底被泄漏的实例（`_disposed` 先置防自动重连复活；与 SerialPort 的 Component 终结器对齐），并用实证测试**钉死运行时事实：已连接实例被挂起的读操作 GC 根化、永不终结**——终结器只覆盖未连接/读已完成的泄漏，残余场景（泄漏且已连接，当前组合无生产者）留给规划的固件侧空闲超时兜底；进程崩溃由 OS 关闭句柄发 FIN/RST，桥的 lwip 即释放客户端槽
  - **桥固件 v2 协议适配 + 真机验证（2026-10-27）**：握手升级为 `<baud>[E|O|N]`（open_port 的 parity 参数现驱动后缀，如 19200 8E1 = "19200E"）；**精确字节、无结尾换行**——v2 解析器对整个首包校验（`num_digits==len`），内联换行必失败且整包泄漏给目标板（修正固件侧文档"首包发 19200E\n"的表述）。真机实测：acccom 服务路径连接真桥发 `19200E`，**桥自身控制台（COM18）输出 `UART: change line:19200 8E1` 实锤协商成功**，收到 522B JJ400 数据流，干净关闭。**真机暴露固件 v2 两大缺陷（待固件 v3）**：① 对端断开（含干净 FIN）不释放单客户端槽位（`netconn_recv` 失败走 `ERR_CLSD → continue` 跳过 `uart_bridge_reset`），一次启动只服务一次连接、恢复需重启 DAP——v2 桥上 TCP 的 set_baud 透明重连必失败；② 槽位被占时新连接的 accept-即踢路径疑似可致固件 panic 重启（实测 13:31:30 rst:0xc 与碰撞窗口重合，未定论）
  - **桌面 GUI 支持 TCP 串口端点（2026-10-27 三轮）**：`MainWindow` 主服务由裸 `SerialService` 换成 `EndpointSerialService` 分发器——连接栏/发送/HTTP API 全部经 ISerialService，一行换装即通；端口下拉改**可编辑**（`Text` 绑定 + `TextSearch.TextPath`，可直接手输 `tcp://host:port`，COM 列表照旧；手输端点经 `LastPort` 设置持久化，重启保留）。GUI 波特率/校验位选择器自然流入首包握手（实测 HTTP `/api/port/open` 开真桥 19200 8E1，回环见 `57600E`、真桥 4s 内 rxCount=4、干净关闭）；切换波特率 = 改选后重开（自动重握手），live 换速走 MCP `set_baud`。GUI 与 agent 会话共用单客户端槽位的约束不变
  - **桥固件 v3 对齐 + set_baud 300ms 重连宽限（2026-10-27 二轮，真机验证）**：固件 v3（md5 0cf8c045）槽位释放三路径（FIN/RST/write fail）修复后，TCP 上 `set_baud` 透明重连正式可用——`SetBaudRate` 拆连接后、重拨前固定留 **ReconnectGraceMs=300ms**：桥处理 FIN 有 ~200ms busy 窗口，且旧 FIN 与新 SYN 走不同 TCP 连接、WiFi 重传可让 SYN 抢先（被踢无法在连接时同步检测，只能预防），契约以测试下界钉死。真机：open 19200E → set_baud 38400E **338ms 完成**（300 宽限+38ms 重拨握手），连接存活；无宽限版本亦实测 16ms 重拨成功（FIN/SYN 同路径 FIFO 使 FIN 先达，宽限为确定性保险），COM18 双 `change line` 协商行见证。**新观察待定性（已转固件会话）**：干净生命周期后空闲 2~6 分钟，监听器曾对 SYN 静默不响应（ping 通、COM18 零日志、超时而非被拒、期间一次自愈，随后芯片自行复位恢复）；复现实验 +30s/+90s/+180s 均未再现。文档/skill 全面对齐 v3（integration.md、wireless-dap §6、acccom-serial TCP 节）
  - 顺手修 **ParserEngine 非正超时测试的时机假设**（既有缺陷，干净 HEAD 隔离也挂）：熔断改造后脚本经 Task.Run 上线程池，1ms 钳制窗口内完成不再有保证——超时返回 null 是设计内行为；契约改回「不抛」（null-或-结果皆可），解析正确性用默认超时硬断言

### Fixed

- **无线桥端点只存在于手输记忆——端口下拉里始终看不到它**（`fix(gui)`，2026-10-27）：`PortOptions` 只从 `SerialPort.GetPortNames()` 填充，`tcp://host:port` 端点永远不在 OS 串口列表里，因此用户除了手输之外无法从下拉选中无线桥；`LastPort` 一旦被任何 COM 选择覆盖（或插件在空选择时自动选第一个到达口），端点就彻底从界面上消失。修复：新增 `AppSettings.KnownTcpEndpoints`（最多 8 个，最近优先）持久化用过的端点，`ConnectionViewModel.KnownTcpEndpoints` + `RebuildPortOptions()` 把它们排在 COM 口**之前**注入下拉（带「TCP 端点（无线桥）」说明）；登记点覆盖面板开连成功、HTTP API 采纳、关窗口保存活的 `CurrentPort`；端口插拔监控与手动刷新统一走重建，插拔不再冲掉端点。`PortOption.ToString()` 返回 `Display`（UIA/读屏不再念 record 属性转储）。真机验证：冷启动展开下拉即见 `tcp://192.168.0.125:1234 — TCP 端点（无线桥）` 与 `COM17 — USB-SERIAL CH340` 两项。主套件 1561 不变
- **GUI 记住的端口在重启后丢失（TCP 端点永不恢复）**（`fix(gui)`，2026-10-27）：`LastPort` 保存时无条件写入，但恢复要求**端点存在于系统 COM 列表**——`tcp://host:port` 永远不在 COM 列表，于是存得下、回不来，端口栏空置后被 PortMonitor 的「无选择时自动选第一个到达口」顶成新插的 COM 名。修复两层：① 恢复条件放宽为「合法 TCP 端点 **或** COM 名仍在列表」；② 保存点（窗口关闭）在串口仍开着时改存**实际连接的端点**（`_serial.CurrentPort`）——采纳场景下面板选择器可能还留着 API 开桥前的旧 COM 名，存它会覆写掉 `tcp://` 端点。测试不变（1561），冷启动真机验证：端口栏自动回填 `tcp://192.168.0.125:1234` + 19200 8E1。
- **GUI 面板对非面板路径的开关连接零感知——标题徽章永远「未连接」**（`fix(gui)`，2026-10-27）：HTTP `/api/port/open|close` 直改串口服务，`ConnectionViewModel.IsOpen` 只由面板自己的开关按钮维护，于是 API 开桥后面板字段停在默认值、徽章恒灰、计时器不走（真机截图暴露：底部「已连」与顶部「未连接」同屏矛盾）。修复两层：① `HttpService` 新增 `OnConnectionAdopted` 事件（open 成功携**实际生效的 ActiveConfig**——含已开早退分支，回显的是真实状态而非被忽略的请求；close 成功发 null），`MainViewModel` 订阅转 `ConnectionViewModel.AdoptExternalConnection`：经 Dispatcher 切回 Serial 模式、把端点/波特率/校验位填进选择器、置 IsOpen + 启动计时（断线方向本就有 `_serialDisconnectedHandler` 先例，此为补连接方向）；② 顶部徽章修**独立老 bug**——它住在 `ChromeTitleBar.CenterContent`（DP + ContentControl 绑定的承载链）里，Style 触发器上的继承 DataContext 绑定挂上后**永不重估**（可视树探针实证 DataContext 正确却恒灰），给宿主显式 `DataContext = _vm` 免疫；顺带补文字切换（原设计只变色不换字）：新增 `Conn.BadgeConnected` 中英键，连接态显示绿「已连接」。测试 +3（采纳携生效配置/已开早退采纳真实状态/关闭发 null），主套件 1561
- **dsh-serial-panel v1.8.2：写路由无同源防护 + 增量整流分配 + 两处体验缺陷**（`fix(dsh)`，2026-10-24）
  - **安全**：`POST /clear` 与 `POST /mcp` 此前只校验 method——任意网页可跨站静默截断共享日志、或把 MCP 关掉（实测 `Origin: https://evil.example` + `text/plain` 直接 200）。现按 `Sec-Fetch-Site` → `Origin` → `Referer` 判同源，跨站写一律 403；curl/agent 不带浏览器标记仍放行（AGENTS.md 干净复现工作流不变）。**桌面端 `dsh-app://app` 必须先于 `sec-fetch-site` 显式放行**——桌面壳跨 scheme 访问必然带 `cross-site`，顺序写反会让桌面版开关「点了没反应」（壳的 `forwardWebRequest` 本就会剥掉 `host/origin/cookie/sec-fetch-site`，两条路径都实测过）。
  - **`POST /mcp` 语义收紧**：body 必须是显式布尔，缺字段/非布尔/坏 JSON 回 400。旧实现把解析失败一律读成 `enabled:false`，一次畸形请求就等于越权关闭 MCP。
  - **稳健性**：尾随改 1MiB 分块读 + 流式 `TextDecoder`（跨块边界的多字节字符不再被劈成 U+FFFD）；body 上限 64KiB，超限回 413 **并排空请求体**（不排空会污染 keep-alive，下一请求 ECONNRESET——真实 `http.Server` 实测发现，桩测不出）。
  - **体验**：过滤无命中时不再显示「尚未发现 mcp-traffic.jsonl」的假诊断（拆成两态 + 一键清除过滤）；列表新增「加载更早」+ 滚动锚定（此前只挂载最近 300 行，5000 条环里更早的条目在 UI 上不可达）；`Row` 的内联 `onPort` 箭头改 `useCallback`，否则 `React.memo` 每轮失效、输入一字即重渲 300 行；chip 补 `aria-pressed`；面板实时显示时图标 5s 轮询让位。
  - **测试** 69 → **125 断言**（新增 `mcp`/`chunk`/`http` 三模式）。`chunk` 把汉字精确钉在 1MiB 块边界、`http` 用真实 `http.Server` 打洞——两者都用变异测试证明断言有牙（去掉流式解码 → chunk 失败 18288 vs 18290；去掉排空 → http 失败）。另有真实 React 19.2.6 静态渲染验证（主面板/tab 体/侧栏图标三路径，无 hooks 违规）。
  - 文档对齐：README/AGENTS.md 补 v1.8.2 章节与同源约束说明；`docs/guide/architecture.md` 版本号与断言数（1.6.0/67 → 1.8.2/125）；web profile 部署副本从 v1.1.0 同步到 v1.8.2。
- **解析器死循环可逃生 + 连续超时熔断**（`fix(core)`，2026-10-24 二轮）：实测证伪原假设——Roslyn `RunAsync` 对无 await 的脚本在**调用线程同步执行**，CPU 死循环（`while(true);`）会把组帧管线永困在 RunAsync 内部，5s 超时与一切防线都够不着。`ExecuteAsync` 把脚本提升到线程池（`Task.Run`），`WaitAsync` 期限对 CPU 脚本真正生效；代价是每次执行武装一个期限源（原「快脚本零定时器」优化与可逃生物理不可兼得，契约测试改写为「快完成 + 每次至多一个期限源」）。熔断：连续 3 次超时自动停用该解析器（成功/重载/清空复位计数），状态下拉框复位 + 状态栏提示（新增 zh/en 语言键）；坏脚本代价从「每帧一根永困线程」封顶到 3 根（脚本线程本身仍不可中止——.NET 无安全中止手段，这是上限而非消除）
- **SerialService 三处生命周期缺陷**（2026-10-24 审计 P1/P2，`fix(core)` 358e766）：
  - **重连循环在 Close() 取消后仍可能把新打开的端口换上 `_port`**——取消检查只在循环头，`tempPort.Open()` 成功到换口之间无复查；用户已点关闭时新口被换上，留下一个「已关闭却仍打开」的端口，且与用户 Close 在 `ClosePortOnly` 内交错。换口抽取为 `AdoptReconnectedPort` 并在换口前复查令牌，取消则丢弃新口
  - **`Close()` 同步阻塞调用线程最长 2 秒**——排空等待 `GetAwaiter().GetResult()` 在 UI 线程执行（`ConnectionViewModel.ToggleOpenClose` → `SerialConnectionManager.ToggleConnection` 同步调 Close），洪流下关端口冻窗到排空上限。`ISerialService` 新增 `CloseAsync`（默认包同步 Close，轻量实现零成本），SerialService 覆写为真异步排空，关闭分支改 await——与 SendAsync 的既有先例同因同解
  - **并发写串口无串行化**——触发器发送（RX drain 线程）与 UI 发送、并发 MCP 工具调用（SDK 按调用并发派发）可交错写串口（`SerialPort.Write` 无线程安全保证）。`Send/SendAsync` 单写包进 `_sendGate`，重试等待留在门外；交错防护本身**待真机验证**（无硬件只能证干净失败）
- **触发器/宏/协议测试三处发送缺桥路由**（R167 遗留，`fix(gui,core)` 0822b9c）：桥接模式下这三路发送直连 `_serial.Send`、数据不出网桥（TriggerViewModel:121 / MacroViewModel 宏步骤 / ProtocolTestViewModel.SendCallback）。新增 Core `SendRouter` 统一发送通道（`bridge.IsConnected ? bridge.Send : serial.Send(SendAsync)`），三处补路由 + 主发送/快捷/循环三处内联分支全部委托，六处一个口径；两条路径都自产 TX LogEntry，计数/日志口径不变
- **Modbus 从站 FC03/FC04 异常码语义颠倒**（`fix(core)` b191a61）：数量超限（>125/0）现在报 0x03 illegal data value、区间越界报 0x02 illegal data address——原映射恰好相反，且与 FC01/FC02 的数量分支不一致；FC01/FC02 区间越界同改 0x02。既有测试 0x07D0→0x07D1（2000 是合法上限，旧请求实为地址越界场景，名不副实），新增 6 个异常码契约测试
- **7 处源码编码损坏字节**（em-dash/右箭头第三字节被替换成 '?'、1 处 GBK 编码 em-dash；node 字节级扫描定位并修复），全库无效 UTF-8 清零（`fix(core)` b191a61）

### Changed

- **MCP 工具描述瘦身**（`perf(mcp)` b0315d7）：11 工具 name+description+inputSchema 总字节实测 **8757 → 7918（-9.6%）**，契约语义零删减（默认值/范围/错误码/游标语义全保留）；wait 类工具描述补上实现既有的 timeoutMs 下限（clamp 100-60000，原描述只写 max）；tag 参数描述统一短式；`send/send_and_wait` 的 hex 流量记录改走 `HexForTrafficLog`——解码字节按规范空格排布（原样透传任意空格输入），与串口/虚拟口 TX 条目同格式
- **NetworkBridge/虚拟串口的 LogEntry 生产端补齐 ByteCount**（b191a61）：消费端 `ResolvedByteCount` 不再为桥/虚拟流量逐包回退重扫 hex（SerialService RX 自 R168 起已携带）
- **五个时敏测试的「证明已发生」门加宽**（5-10s → 15-30s，b191a61）：全量并行下的线程池饥饿误报清零——RX 排空等待、MultiPort 慢发/慢关、RecentRx 并发、Parser 热重载防抖（防抖上限 2→3 保留饥饿余量，仍远小于每写一 reload 的 5）。真回归均为永久阻塞，有限窗口照样捕获

### Added

- **MCP `set_baud` 工具（第 12 号）**（`feat(mcp)`）：开口状态免复位变波特率——`SerialPort.BaudRate` 直改 DCB 不重开端口，不触碰 DTR/RTS，避免「探测波特率把设备打复位」；`ISerialService.SetBaudRate`（默认 false，测试桩零成本），SerialService 同步 `_lastConfig`（否则自动重连用旧波特率复活连接）；非正数 INVALID_CONFIG、无开口 PORT_NOT_OPEN、成功回 `{ port, baudRate, appliedBaudRate, tag }`（回实际生效值，与 open_port 的 ActiveConfig 纪律一致）；+4 测试（全 VirtualSerial 免硬件；DTR 不复位语义待真机验证）。12 工具 schema 实测 **8438 字节**，仍低于本轮 8757 基线
- **MCP 常驻副本方案落地（plan 7.4 调整版）**（`chore`）：`tools/publish-mcp.ps1` 一键发布到 `%LOCALAPPDATA%\ACCcom\mcp-server\`（任何构建/git 操作碰不到的位置），`.claude/settings.json` 改指该副本（顺带清掉 R49 已删除的 `--proxy` 死参数）——MCP 客户端不再锁 `bin\*`，`dotnet build` 的 MSB3027/3021 反复撞锁根因消除；纪律入 CONTRIBUTING（改 McpServer 后重跑发布，防客户端连旧服务器）；仓库外 `D:\codeSMwork` 真源仍由用户按 R5 纪律自行修改
- **docs/audit-2026-10-24.md：全面审计报告**（基线数据 + 九维度发现 + 已知遗留核实 + 修复对照表）；待确认项余：MCP 数值序列发送工具（缓，待具体设备场景）
- 测试 +18（Core +16 / McpServer +2）：重连采纳/取消丢弃、空口 CloseAsync、并发发送干净失败、SendRouter 三向路由（TCP 回环实测）、从站异常码 ×6、生产端 ByteCount ×4（含 TCP 回环双方向）、HexForTrafficLog ×2

- **三处长期无人发现的渲染缺陷**（均经真机截图核对，非代码推断）：
  - **深色主题下连接区 8 个下拉框退回系统 Aero 白底黑字**。`ConnectionPanel` 的 `FieldCombo` 是带 `x:Key` 的样式，会整体替换隐式 ComboBox 样式，又缺 `BasedOn`，于是 `App.xaml` 里带 `ControlTemplate` 的隐式样式被丢弃。昼白主题下白底恰好等于主题色所以从未暴露，五套深色主题里全部刺眼。`SchemaEditorWindow` 的 `PreviewTextBox` 同类问题一并修复
  - **全应用 11 处空态从未显示过**。8 个文件里的空态 TextBlock 同时写了本地属性 `Visibility="Collapsed"` 与 `DataTrigger` 显示逻辑；WPF 属性优先级里本地值高于 Style 触发器，本地值永远赢，DataTrigger 因此从未生效——主窗口约 60% 面积是空洞，右侧栏 1150px 空白同样无提示。Collapsed 默认值收进共享的 `EmptyListText` 样式（样式 setter 优先级低于触发器）
  - **9 个 MDL2 图标渲染成豆腐块 □**。私有区码位（U+E7xx 等）必须配 `Segoe MDL2 Assets`，直接写 `Content` 会用继承的 Segoe UI 渲染。涉及 `ConnectionPanel` 4 个（预设保存/删除、解析器目录、帧拼接配置）+ 5 个走 `ChromeTitleBar.ExtraButtons` 插槽的标题栏按钮。在 `IconBtn` 与 `TitleBarButton` 两个共享样式上补 `FontFamily`，一次覆盖全部调用点
- 状态栏 RX/TX/错误三个计数器**永久显示下划线**：`TextDecorations="Underline"` 写成本地属性而无条件生效，任何时候都像超链接；改为 hover 才显示（下划线本就是点击暗示）

### Changed

- 界面第二轮（阶段 6-8）：**数据面板表头由三条横带压成一条**（分栏开关内联到面板标题左侧，四个 JSON/CSV/PCAP/TXT 导出按钮合并为一个「导出全部」下拉——原先四个按钮只靠后缀区分意图，且这一组只对合并视图生效，而分栏的 RX/TX 各自已带一套导出）；**空态从孤零零一行字改为仪器式闲置态**（发丝线 + 说明「打开串口后，收到的每一帧会逐条显示在这里」+ 三个关键快捷键，占据主窗口约六成面积的那块空白终于有了内容）；**状态栏由双行堆叠改为单行仪表读数**（单位 B / B/s / fps 此前与数字同号同重，一眼分不出「0 B」和「0.0 B/s」，现单位一律降为 UnitText，六个满高分隔线改为留白式分组）；**侧栏去掉最后一张阴影卡片**、标题改 PanelTitle（此前是 Accent 紫，与数据面板标题争主色）、四个裸字形翻页按钮由 26x24/14px 收到 24x22/12px（原尺寸让它们成为 260px 侧栏里最大的视觉元素，读起来像漂浮字符而非控件）、计数之间加发丝分隔（原为「1/1 0」两个无标签数字并排）
- **数据行改为等宽表格**：id 列此前是 `Auto`，计数器从 #1 涨到 #10000 时时间戳与载荷被整体右推，每变一次数字就要重新找载荷位置。现固定 56px 并右对齐，数字本身成列
- 修复两处渲染为空的图标字形：书签列表按钮的 MDL2 私有区码位在 Segoe MDL2 Assets 里没有字形，渲染成空心矩形，读起来像坏掉的按钮——改为带文字的「书签」按钮（旁边的箭头自解释，「我在哪个书签上」不自解释）；录制按钮同理改为一个 8px 圆点（录制是状态不是图标，无需字体也就不会退化成豆腐块），录制时转红
- `Button.Modbus` 由全大写 `MODBUS` 改为 `Modbus`——它是工具栏上唯一的拉丁按钮，全大写与相邻中文标签并列时格外扎眼
- 快捷发送侧栏搜索框补 watermark。首版直接复用了 `Tip.FilterByName` 长句，在 260px 侧栏里截断到半个字（与上一轮数据面板搜索框同一个错误），改用短提示「搜索指令…」

- 界面「仪器面板化」（阶段 0-5）：命令栏两行合并为同一 Surface（此前是两个各自带阴影的色带，读起来像两层堆叠工具栏）；发送栏/附加端口行由圆角阴影卡片改为平面色带 + hairline——数据面板、发送栏、附加端口三张卡同列出现四种高度层级，没有任何一面读得出主次，浮起现在只留给真正的浮层（菜单/下拉/气泡）；面板信号条由 3px 双半色改为 1px 单色（合并视图里那条一半蓝一半灰的色带暗示了视图并不存在的左右分栏；2px 时又成了全窗最响的一条线，与面板标题争注意力）
- **Accent 单色化 + 四处语义撞色修复**，新增可复跑校验 `tools/theme-audit/check-theme-contrast.ps1`（按钮文字走标准 WCAG 相对亮度要求 >= 4.5:1；Accent 与语义色用 HSL 色相距离判定可混淆，阈值 25°）。校验暴露：KlimtKiss `Accent` 与 `StatusError` **字节级相同**（都是 #E5484D）——开口按钮穿 Accent、闭口态穿 BtnDanger，全应用最重要的开关因此有两张几乎一样的红脸，状态切换读不出来；改金箔 #D9A441 后与 StatusWarning 仅差 6°，降琥珀仍只差 2°（琥珀与金是同一色相的两个明度），最终警告色改用拜占庭紫 #B07BD8；MonetSunrise Accent 与警告差 25°、与错误差 17° 正好卡在阈值上，试过 #B03A6B 与 #C2410C 均不合格，最终走出暖色族改用阴影蓝 #1F5FA8；VanGoghWheat 试青铜色 #8A6A12 被校验直接报出按钮文字 3.63:1（低于 AA），改深青 #2E6B63。七套主题现全部通过（按钮文字 5.18~8.40:1）
- 排版角色落地：面板标题改 `PanelTitle`（12px Bold + 主墨色，此前与正文同为 11px 靠颜色区分，层级读不出来）；计数改 `ValueText`（等宽半粗）；单位后缀（ms / B / s / fps）改 `UnitText`（9px 等宽三级墨，原先与数值同号同重，单位在抢数字注意力）；`SectionHeader` 由 11px 改为 10px 雕刻式微标签
- 文案分层：三个搜索框 watermark 由完整 Tip 句改为「搜索…」（原文案把「仅支持正则」和快捷键一起塞进 placeholder，在 180px 框里被截断到半个词，读起来像布局故障）；附加端口两个输入框补短提示「标签」「端口名 / 地址」——原本完全没有提示，渲染成两个空白矩形像坏掉的控件，首次尝试复用 Tip 长句仍溢出框外故改短
- 动效收敛：移除 PrimaryButton / OutlineButton / HeaderButton / OpenCloseButton 的 hover 放大 1.02 + 上浮 1px 弹跳（密集命令栏上按钮是连点burst，弹跳读起来是抖动而非打磨，且让相邻 1px 分隔的控件看起来在相对移动，破坏平面几何）。改为两处有意义的动画：面板信号条在有数据流入时做透明度脉冲（`RxCount`/`TxCount` 变化经 Dispatcher 节流——这两个属性每收一帧就变一次，突发洪流下是每秒数百次通知，而信号条只需知道「开始流了/停了」而非确切速率；只动 Opacity 不动几何，绝不位移下方行）；录制呼吸灯保留并改用 MotionSlow + SineEase。12 处控件圆角改走 RadiusChip/Control/Panel token（按行定位，避免误伤焦点环与滚动条轨道）
- 新增 Tokens 间距 scale（0/2/4/6/8/10/12/16/20 + 单边变体）与圆角 scale、动效时长 token。取值沿用既有主导值，属命名化而非重排——此前 3/4/5/6/7/8/9/10/11/12/14/16/20/22/26px 混用，面板看起来不像同一套网格
- README/CONTRIBUTING 测试徽章 1385/1372 → 1398（实测 `ACCcom.Core.Tests` 1286 + `ACCcom.McpServer.Tests` 112）

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
