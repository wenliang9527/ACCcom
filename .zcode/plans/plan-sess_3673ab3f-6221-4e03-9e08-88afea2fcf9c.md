## 第 15 轮:快捷键总览面板 + 修复 Ctrl+F 死代码(体验死角补完)

### 现状(已通过代码探测确认)

- **Ctrl+F 是死代码**:`MainWindow.xaml.cs:198-202` 拦截 Ctrl+F 后只 `e.Handled = true`,注释写着 "Search box is now in DataPanel - need to expose it" — 但 `FocusDataPanelSearch(rx:)` 方法早已存在(Ctrl+1/2 在用),只是没接 Ctrl+F
- **快捷键已积累 33 个**(Ctrl+1/2、Ctrl+R、Ctrl+B、Ctrl+Left/Right、Ctrl+Shift+H/K/T/E、F3/Shift+F3、Ctrl+L/Ctrl+Shift+L、Ctrl+D、Ctrl+S/Ctrl+Shift+S、F5、Esc、Enter、Alt+1-9、Up/Down 历史导航...),**但没有可查的总览** — 用户只能靠 tooltip 逐个发现
- F1 未被占用;状态栏右侧有 HTTP URL 展示区(可加 ? 入口)

### 实施方案(一个小窗口 + 一行死代码修复)

#### 1. 修复 Ctrl+F(死代码 → 真实行为)
- `MainWindow.xaml.cs` Ctrl+F 分支改为 `FocusDataPanelSearch(rx: true)`(与 Ctrl+1 完全一致:聚焦 RX 搜索框 + 全选)
- 顺带 Ctrl+F 不再需要 Ctrl+1 的独立分支重复 — 保持现状,只补 Ctrl+F 行为

#### 2. 新增 ShortcutsWindow.xaml(.cs)
- chromeless 标题栏(复用 StatsWindow 模式)+ 快捷键列表
- 数据源:`MainViewModel` 暴露 `IReadOnlyList<ShortcutInfo>`(新 record `ShortcutInfo(string Keys, string Description)`)
- 列表分组显示:发送/数据操作/导航/工具窗口/其他(用 ItemsControl + GroupStyle 或直接分类 StackPanel)
- 每行:快捷键(等宽 Consolas,Accent 色)+ 描述

#### 3. ShortcutInfo 数据源定义
- 放在 MainViewModel 或独立静态类 `ShortcutCatalog`(静态只读列表,与 MainWindow 实际热键一一对应)
- 描述走 i18n 键(`Shortcuts.Send`、`Shortcuts.CopySelected`...),避免硬编码

#### 4. 入口
- 状态栏右侧 HTTP URL 旁加「快捷键 ?」TextBlock(点击打开,复用 OnHttpUrlClick 的 code-behind 模式)
- MainWindow `PreviewKeyDown` 加 **F1** → 打开快捷键窗口(与标准帮助键一致)
- MainViewModel 加 `OpenShortcutsCommand` + 单例窗口

#### 5. i18n — zh-CN.json + en-US.json
- `Shortcuts.Title` / `Status.ShortcutsOpened` / `Shortcuts.Send` / `Shortcuts.CopySelected` / `Shortcuts.ClearRx` / `Shortcuts.ClearTx` / `Shortcuts.ToggleHex` / `Shortcuts.ToggleHexSend` / `Shortcuts.SaveRx` / `Shortcuts.SaveTx` / `Shortcuts.RefreshPorts` / `Shortcuts.ToggleTheme` / `Shortcuts.AddBookmark` / `Shortcuts.PrevBookmark` / `Shortcuts.NextBookmark` / `Shortcuts.ToggleRecording` / `Shortcuts.OpenHighlights` / `Shortcuts.OpenProtocolTest` / `Shortcuts.OpenTriggers` / `Shortcuts.JumpRx` / `Shortcuts.JumpTx` / `Shortcuts.FindNext` / `Shortcuts.SendQuickCmd` / `Shortcuts.HistoryNav` / `Shortcuts.StopLoop` / `Shortcuts.OpenShortcuts` ~25 条

#### 6. 测试
- 数据源是纯静态列表(无 WPF 依赖),补 1 个测试:`ShortcutCatalog_Descriptions_ResolveInBothLanguages` — 遍历所有 i18n 键确认 zh/en 都存在(防止漏翻译)
- ShortcutCatalog 放 Core(纯数据 + 语言键),MainViewModel 引用它 → 可测

### 不做(避免越界)

- **不做**可自定义快捷键(超出本轮,只做查表)
- **不做**快捷键冲突检测
- **不做**把 Alt+1-9 等改成绑定式(只做文档化)

### 验收标准

1. `dotnet build -c Release`:**0 警告 0 错误**
2. `dotnet test`:Core ≥ 491 + MCP 47 = ≥ 538 全绿
3. 手动验证:按 F1 弹出快捷键总览;状态栏 ? 也可打开;Ctrl+F 聚焦 RX 搜索框
4. 工作树干净,1 个 commit