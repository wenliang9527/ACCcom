## 第 11 轮:ProtocolTestRunner UI(自动化协议回归测试)

### 现状(已通过代码探测确认)

- `src/ACCcom.Core/Services/ProtocolTestRunner.cs` 后端完整:`RunAsync(script, send, waitForResponse, ct)` 逐步骤执行、断言、重试、取消,产出 `TestReport`
- `src/ACCcom.Core/Models/TestScript.cs` 模型完整:TestScript(步骤列表/重复次数)/ TestStep(命令/期望模式/匹配方式/超时/重试)/ TestStepResult / TestReport
- `tests/ACCcom.Core.Tests/ProtocolTestRunnerTests.cs` **22 个测试**覆盖 send-only、contains/exact/regex/hex_contains、mismatch、timeout、retry、delay、cancellation、persist
- **UI 完全缺失**:grep 确认 src/ACCcom(VM/View/Controls)零引用 — 这是最后一个"后端完整但无 UI"的大功能
- 可复用的发送 API:`ISerialService.Send(string data, bool isHex)`(串口)
- 可复用的匹配器:`PatternMatcher.MatchesPattern(target, pattern, matchMode)` — 但缺 `hex_contains` 分支 + 缺 LogEntry 便捷入口
- MainViewModel 已有 `OnEntryProcessed` 挂钩(用于 recorder),是注入 RX 流的现成点

### 实施方案(窗口 + VM + Core 补丁 + 测试)

#### 1. Core:ProtocolTestRunner 补静态匹配入口(可测)
- 加 `public static bool TryMatchEntry(LogEntry entry, string pattern, string matchMode, bool matchHex, out string? matchedText)`
  - target = matchHex ? entry.RawHex : entry.Text
  - 内部走 `PatternMatcher.MatchesPattern`,并补 `hex_contains` 分支(已有 contains 语义,显式映射)
  - matchedText = target(供 TestStepResult.ActualResponse 展示)
- 补 2 个测试:`TryMatchEntry_hex_contains`、`TryMatchEntry_regex`(放入现有 ProtocolTestRunnerTests.cs)

#### 2. 新增 ProtocolTestViewModel
- 字段:`ProtocolTestRunner _runner`、`ISerialService _serial`、`ConcurrentQueue<LogEntry> _rxQueue`、`CancellationTokenSource? _cts`、`Func<bool> _getIsOpen`、`Action<string> _setStatus`
- 脚本编辑属性:`ScriptName` / `Description` / `RepeatCount` / `RepeatDelayMs` / `Steps`(ObservableCollection<TestStep>)
- 结果集合:`ObservableCollection<TestStepResult> Results` + `IsRunning` + `PassedCount`/`FailedCount`
- 命令:`AddStepCommand` / `RemoveStepCommand` / `RunTestsCommand` / `StopTestsCommand` / `NewScriptCommand` / `SaveScriptCommand` / `LoadScriptCommand`(JSON 存到 %LOCALAPPDATA%/ACCcom/scripts/)
- `OnRxEntry(LogEntry)`:入队(平时零开销,单次入队)
- `RunAsync`:清空 Results → `_runner.RunAsync(script, send, waitForResponse)`;每步结果追加到 Results 并刷新计数;IsRunning 翻转;状态栏反馈
- `waitForResponse`:轮询 _rxQueue,用 `ProtocolTestRunner.TryMatchEntry` 匹配,命中返回文本,超时返回 null

#### 3. 新增 ProtocolTestWindow
- chromeless 标题栏(复用 StatsWindow 模式)
- 上部:脚本元信息(Name/Description/RepeatCount/RepeatDelayMs)+ 按钮行(新建/加载/保存/运行/停止)
- 中部:Steps DataGrid(可编辑列:Name/Command/IsHex/DelayMs/ExpectedPattern/MatchMode/ResponseTimeoutMs/RetryCount/RetryDelayMs)+ 添加/删除步骤按钮
- 下部:Results DataGrid(StepName/Passed✔✘/Attempts/Duration/FailureReason)+ 汇总(Passed/Failed 计数)
- code-behind 走 `WindowHelper.SetupTitleBar` 模式

#### 4. MainViewModel 接线
- 字段 `ProtocolTestViewModel? _protocolTest`
- `OpenProtocolTestWindow()` 单例(参照 ModbusWindow 模式):lazy 初始化 VM(需 `_serial`、`_tool.IsOpen`、StatusText)
- 在 `OnEntryProcessed` 加一行 `_protocolTest?.OnRxEntry(entry)`(null 检查,平时零开销)
- 透传命令 `OpenProtocolTestCommand` + 属性 `ProtocolTest`

#### 5. ToolBarPanel 加按钮 + 热键
- 在 HighLight 星标旁加 `&#x2714;`(对勾)按钮绑定 `OpenProtocolTestCommand`,Tip 提示 Ctrl+Shift+T
- MainWindow PreviewKeyDown 加 `Ctrl+Shift+T` → OpenProtocolTestCommand

#### 6. i18n — zh-CN.json + en-US.json
- `Tip.OpenProtocolTest` / `Button.ProtocolTest` / `Status.ProtocolTestOpened` / `ProtocolTest.Title` / `ProtocolTest.Run` / `ProtocolTest.Stop` / `ProtocolTest.NewScript` / `ProtocolTest.LoadScript` / `ProtocolTest.SaveScript` / `ProtocolTest.AddStep` / `ProtocolTest.RemoveStep` / 列头 ~10 条 / `ProtocolTest.Running` / `ProtocolTest.Completed` / `ProtocolTest.NoScriptsDir` 等 ~25 条

#### 7. 测试
- ProtocolTestRunnerTests 补 2 个(TryMatchEntry hex_contains + regex)
- 不改其他测试;UI 层逻辑全部下沉到 Core 可测

### 不做(避免越界)

- **不做**脚本语法高亮 / 步骤拖拽排序(DataGrid 行序已够)
- **不做**报告导出 CSV/HTML(现有 SaveReport JSON 已够)
- **不做**解析器联动断言(仅文本/hex 匹配,与后端语义一致)
- **不做**从录制回放驱动测试(留待后续)

### 验收标准

1. `dotnet build -c Release`:**0 警告 0 错误**
2. `dotnet test`:Core 480 + MCP 47 = **527 全绿**
3. 手动验证:连串口 → 打开协议测试窗口 → 新建脚本 → 加步骤(Send `AT` + Expected `OK`)→ 运行 → 状态栏反馈、Results 显示 pass/fail
4. 工作树干净,1 个 commit