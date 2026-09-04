## 第 51 轮:收尾上轮两个候选 — 阴影移出动态子树 + 高亮移出入队线程

### 改动 1:数据卡片阴影 → 静态装饰层(任务 A)

**问题**:DataPanel.xaml 的 RX/TX 两张 PaintedCard 整体带 `Effect="{DynamicResource CardShadow}"`(BlurRadius 16-18),ListBox 每 30ms 刷新 → 整卡(含模糊)每帧离屏重渲染。

**方案**(不动 PaintedCard 模板,零波及其他静态卡片):
- `DataPanel.xaml` RX 卡片(17 行)与 TX 卡片(226 行)各包一层 Grid:
  ```xml
  <Grid Grid.Column="0">
      <Border Background="{DynamicResource BgSurfaceBrush}" CornerRadius="8"
              Effect="{DynamicResource CardShadow}" IsHitTestVisible="False" />
      <controls:PaintedCard Background="{DynamicResource BgSurfaceBrush}" CornerRadius="8">…原内容…</controls:PaintedCard>
  </Grid>
  ```
- 装饰 Border 与卡片同 cell 完全重叠(无 Margin,布局不变);阴影从装饰层(纯静态)溢出,卡片本体盖住实体 → 视觉与现状一致,但 ListBox 内容刷新不再触发 Effect 重算
- 保持 `DynamicResource CardShadow`(7 主题切换自动换肤);`IsHitTestVisible="False"` 保证点击穿透
- **不做**:QuickSendSidebar/MainWindow 发送栏/多端口(静态内容,无 30ms 刷新,保持现状)

### 改动 2:ApplyHighlight 移入队线程 + 规则快照(任务 B)

**问题**:高亮匹配在 UI 线程 flush 中逐条执行(规则多时每帧 300×N 次 Contains/Regex);且 `GetHighlightColor` 直接遍历 ObservableCollection,后台读会与 UI 编辑冲突。

**HighlightService.cs**(照 `TriggerService._snapshot` 模式):
- 新增 `private readonly object _lock = new();` + `private volatile HighlightRule[] _snapshot = Array.Empty<HighlightRule>();`
- `AddRule`/`RemoveRule`/`Load`(含 `Rules.Clear()` 分支)在锁内重建 `_snapshot = Rules.ToArray()`
- `GetHighlightColor` 改遍历 `_snapshot`(无锁读,行为契约不变——现有 21 个测试守护:按名替换、删除即时生效、优先级/平级顺序、IsEnabled/Direction 过滤、大小写不敏感)
- `Rules`(ObservableCollection)保留给 UI 绑定,语义不变

**DataFlowViewModel.cs**:
- `FlushPendingEntries` 删除两处 `foreach … ApplyHighlight(entry)`(690/704 行)
- `AddRxEntry`/`AddTxEntry` 在 `lock(_pendingLock)` 前调用 `ApplyHighlight(entry)`(保留 `?.` null 防护)
- 线程安全论证:赋值发生在入队锁之前,UI 取批在同一把锁内 → happens-before 保证条目进入集合前高亮已就绪;回放 TX 的 UI 线程直调与后台 RX 并发读 volatile 快照,安全
- 顺手更新 `GetHighlightColor` 顶部注释(不再"runs on the UI thread")

### 测试
- 现有 21 个 HighlightServiceTests 必须全绿(行为契约不变)
- 新增 1 个并发冒烟测试:`GetHighlightColor_ConcurrentWithRuleMutation_DoesNotThrow`(后台线程循环 GetHighlightColor + UI 线程 Add/Remove,验证快照无撕裂无异常)
- 任务 A 为 XAML 改动,靠构建 + 逻辑验证(阴影参数/布局不变)

### 验收标准
1. `dotnet build -c Release`:0 警告 0 错误
2. `dotnet test`:全绿(566 Core + 10 McpServer,含新增 1 个)
3. 视觉等价:卡片阴影参数/尺寸/位置不变,主题切换仍换肤
4. 工作树干净,commit + push 到 GitHub