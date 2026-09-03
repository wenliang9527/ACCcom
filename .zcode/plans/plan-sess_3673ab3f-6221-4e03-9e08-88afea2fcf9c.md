## 第 13 轮:Trigger 规则 UI(自动化触发器 — 最后一个「VM 完整、UI 缺失」的大功能)

### 现状(已通过代码探测确认)

- `src/ACCcom/ViewModels/TriggerViewModel.cs` **完整**:Add/Save/Load/Delete + `OnTriggerFired` 执行 4 种动作(SendCommand / SaveToFile / PlaySound / LogMessage),`TriggerService` 已在 DataFlowViewModel 的 entry 流里求值,`TriggerPathResolver` 已处理相对路径
- `src/ACCcom.Core/Models/TriggerRule.cs` 模型完整(5 字段 + Direction/MatchHex/ActionParameter/Enabled)
- **UI 完全缺失**:grep 确认无 TriggerWindow、无编辑对话框、ToolBar 无入口 — 只能写 JSON 文件手动配规则
- 不一致点:VM 的 `LoadTriggers()`/`SaveTriggers()` 用相对路径 `"triggers.json"`(落 CWD),而 resolver 用 `%LOCALAPPDATA%/ACCcom/triggers` — 补 UI 时统一到 resolver
- i18n 已有 `Status.TriggersSaved` / `Status.TriggerFired` 等键,缺窗口/对话框键
- 可复用模式:HighlightWindow / ProtocolTestWindow(规则列表 DataGrid + 编辑对话框 + 单例窗口 + 工具栏按钮 + 热键)

### 实施方案

#### 1. 新增 TriggerRuleDialog.xaml(.cs)
- 参照 HighlightRuleDialog:chromeless 标题栏 + Name / Pattern / MatchMode(contains/exact/regex)/ Direction(RX/TX/Both)/ MatchHex CheckBox / Action ComboBox(4 种)/ ActionParameter TextBox(依 Action 切换提示:SendCommand→"要发送的命令",SaveToFile→"文件路径(相对路径存到 %LOCALAPPDATA%/ACCcom/triggers)",PlaySound→禁用,LogMessage→"日志内容")/ Enabled CheckBox
- 校验:Name/Pattern 必填;SaveToFile 或 SendCommand 时 ActionParameter 必填

#### 2. 新增 TriggerWindow.xaml(.cs)
- 参照 HighlightWindow:标题栏 + 规则 DataGrid(Name/Pattern/MatchMode/Direction/HEX/Action/参数/开关)+ 底部按钮(添加/编辑/删除/保存/加载)
- 双击行 = 编辑;StatusText 显示当前触发状态

#### 3. TriggerViewModel 补丁
- 暴露 `OpenEditDialog(TriggerRule)` 供 code-behind 双击调用(现有 `AddTrigger` 已是打开对话框形态,补 Edit 入口)
- `LoadTriggers()`/`SaveTriggers()` 默认路径统一到 `TriggerPathResolver.DataDirectory`(修旧不一致)
- `TriggerWindow` 复用现有 `AddTriggerCommand` / `DeleteTriggerCommand` / `SaveTriggersCommand` / `LoadTriggersCommand`

#### 4. MainViewModel 接线
- `OpenTriggerWindow()` 单例(参照 HighlightWindow)
- 透传命令 `OpenTriggerCommand` + 属性 `Triggers`
- `_tool.LoadTriggers()` 已存在(InitializeAsync 里),无需改

#### 5. ToolBarPanel 按钮 + 热键
- 在 ProtocolTest 按钮旁加 ⚡ `&#x26A1;` 按钮,绑定 `OpenTriggerCommand`,Tip 提示 Ctrl+Shift+E
- MainWindow PreviewKeyDown 加 `Ctrl+Shift+E` → OpenTriggerCommand

#### 6. i18n — zh-CN.json + en-US.json
- `Tip.OpenTriggers` / `Button.Triggers` / `Status.TriggersOpened` / `TriggerWindow.*`(Title/Add/Edit/Delete/Save/Load/列头 ~10 条)/ `TriggerDialog.*`(Title/Name/Pattern/MatchMode/Direction/Action/ActionParameter/Enabled/MatchHex/校验 ~12 条)

#### 7. 测试
- 补 `TriggerServiceTests` 或新测试:默认保存路径统一后 `SaveRules/LoadRules` roundtrip 走 resolver 目录(1-2 个)
- 不改现有 24 个 TriggerServiceTests(若有)—— 先确认现有测试是否覆盖

### 不做(避免越界)

- **不做**触发历史/统计窗(TriggerService 无此后端,先做最小可用)
- **不做**拖拽排序(DataGrid 行序够)
- **不做**规则导入/导出(用 Save/Load 已够)
- **不做**正则语法预览

### 验收标准

1. `dotnet build -c Release`:**0 警告 0 错误**
2. `dotnet test`:Core ≥ 489 + MCP 47 = ≥ 536 全绿
3. 手动验证:打开 Trigger 窗口 → 添加规则(Pattern="ERROR" + Action=PlaySound)→ 收到含 ERROR 的 RX 帧 → 播放提示音
4. 工作树干净,1 个 commit