## 第 17 轮:性能专项 — 消灭 30ms flush 风暴的根

### 性能探查结论(基于 WPF 源码验证 + 代码审查)

**P0 阻断性 bug(比性能更优先)**:`ObservableRangeCollection.AddRange/RemoveRange`(src/ACCcom/ViewModels/ObservableRangeCollection.cs L13-46)一次抛单事件多条目,而 .NET 8 `ListCollectionView.ValidateCollectionChangedEventArgs` 对 `Add`/`Remove` 且 `NewItems.Count != 1` 抛 `NotSupportedException(RangeActionsNotSupported)` — 已用 dotnet/wpf release/8.0 源码双重核实(`CollectionView.cs` 与 `ListCollectionView.cs` 都先调该校验)。后果:串口突发 >1 条/30ms 时,`FlushPendingEntries` 的批量 AddRange 从 `DispatcherTimer.Tick` 冒泡到 `DispatcherUnhandledException`(App.xaml.cs L29-37)→ 弹崩溃对话框;条目超 10000 后 `TrimBuffer` 的 RemoveRange 每 30ms 触发一次。这正是高频串口下"周期性卡顿/弹框"的根源。

**P1 渲染风暴(由 P0 修复后逐条事件放大)**:`MainWindow.xaml.cs` L55-68 对 Add 和 Remove 都调 `ScrollToBottom`,双面板最多 ~133 次/秒全量布局;`DataPanel.xaml.cs` L55-67 的 `ScrollRxToEnd` 无条件滚动,没有"已钉底"判断 — 用户手动上翻看历史时会被强拉回底部。

**P2 每行分配**:`HexToBrushConverter` L18-19 每次 Convert 都 `ColorConverter.ConvertFromString` + `new SolidColorBrush`,无缓存、无 Freeze;每行 2 个绑定点 × 可见行 × 每 tick 重复 = 数百次/秒 GC 压力。

**P3 匹配层小开销**:`HighlightService.GetHighlightColor` L71-74 每 entry 做 LINQ `Where + OrderByDescending`(分配),规则多时成本线性;`ApplyHighlight` 每包在 UI 线程调一次(DataFlowViewModel L563-564)。

### 实施方案(4 项,全部带测试)

#### 1. [P0] ObservableRangeCollection 移到 Core 并改逐条事件(阻断 bug)
- **移到 `ACCcom.Core`**(它只依赖 System.ObjectModel,无 WPF 依赖),namespace 改为 `ACCcom.Core.Collections`,更新 3 处 using(自身/MainViewModel/DataFlowViewModel)
- `AddRange`:**底层仍批量 `Items.Add`(快)**,但事件改为逐条 `Add(单条, index)`,PropertyChanged(Count/Item[]) 仍只抛一次 — 既保批量写性能,又满足 ListCollectionView 单条事件契约
- `RemoveRange` 同理:底层 `List.RemoveRange` 批量,事件逐条 `Remove(单条, index)`
- **测试**(Core.Tests 现在能引用):`AddRange_RaisesOneEventPerItem`、`AddRange_RaisesSinglePropertyChanged`、`RemoveRange_EventSequence_MatchesObservableContract`、`AddRange_AfterRemoveRange_IndicesCorrect`

#### 2. [P1] AutoScroll 钉底判断(停止滚动风暴)
- `DataPanel.xaml.cs`:`ScrollRxToEnd`/`ScrollTxToEnd` 加钉底判断 — 仅当 `VerticalOffset + ViewportHeight >= ExtentHeight - 8`(接近底部)才滚;用户上翻后新数据不再抢滚动
- `MainWindow.xaml.cs`:CollectionChanged 处理器对 `Remove` action 不滚动(移除顶部条目视觉不变)
- **测试**:钉底判断逻辑抽成 Core 静态方法 `ScrollPendulum.ShouldAutoScroll(offset, viewport, extent)`(纯计算可测 3-4 例)

#### 3. [P2] HexToBrushConverter 静态缓存
- 静态 `ConcurrentDictionary<string, SolidColorBrush>`,解析后 `Freeze()` 缓存;无效/空返回 Transparent(语义不变)
- **测试**:转换器在 UI 工程无法被 Core.Tests 引用 — 改为验证 ColorConverter 解析的不可行,本轮给 `HexToBrushConverter` 加注释说明 + 依赖现有 DataPanel 行为;核心可测部分是把「色值→brush」提取 Core?不 —— 转换器保持 UI 层,用静态缓存(行为简单,靠编译+手动验证)。测试改为给 ObservableRangeCollection 和 ScrollHelper 补足。

#### 4. [P3] HighlightService 去 LINQ + 提前终止
- `GetHighlightColor`:手写循环遍历启用规则,维护最高优先级命中,去掉 `Where/OrderByDescending` 分配;方向过滤提前短路
- **测试**:现有 20 个单测基础上补 `GetHighlightColor_ReturnsHighestPriority_WithoutEnumerableAlloc`(行为等价断言)+ 保持全绿

### 不做(避免越界)

- **不做** DataPanel 行模板大改(TextBox→TextBlock 去 Wrap 等)— 视觉风险大、超出本轮"效率"焦点,列为后续候选
- **不做** 计数 PropertyChanged 批量合并(语义风险,收益小)
- **不做** Filter 全量重扫优化(仅搜索时触发,非热路径)

### 验收标准

1. `dotnet build -c Release`:**0 警告 0 错误**
2. `dotnet test`:**全绿,Core ≥ 497 + 新增 ~9**
3. 手动/逻辑验证:高帧率串口流不再弹崩溃对话框;用户上翻后新数据不抢滚动
4. 工作树干净,commit + push 到 GitHub