## 第 50 轮:UI flush 路径收敛 — 每帧重复工作三连消

### 背景
前几轮已完成 P0-P3(逐条事件集合、钉底判断、转换器缓存、HighlightService 去 LINQ)。本轮针对探索 agent 报告的剩余热点,全部**无视觉变化**:

### 改动 1(最高收益):AutoScroll 每帧 N 次滚动 → 合并为 1 次
**现状**:`ObservableRangeCollection.AddRange` 逐条抛 Add 事件,`MainWindow.xaml.cs:55-67` 的 CollectionChanged 处理器对**每条** entry 调一次 `ScrollRxToEnd()` → 每 tick flush N 条就执行 N 次 `ScrollToBottom`+布局失效(默认钉底时每次都真正滚动)。已核实 `RxEntries/TxEntries` 的 AddRange **只在** `FlushPendingEntries` 内部调用,无其他写入路径。
**改法**:
- `DataFlowViewModel` 加 `public event Action? BatchFlushed;`,`FlushPendingEntries` 在 Rx/Tx 两个分支处理完后触发一次
- `MainWindow.xaml.cs` 删除两个 CollectionChanged 滚动处理器,改订阅 `BatchFlushed` → 一次 `ScrollRxToEnd()`(仍受 `AutoScrollRx` 开关和钉底判断约束)

### 改动 2:状态栏计数 33Hz 绑定 → 1Hz 统计 tick
**现状**:`RxCount/RxByteCount/TxCount/TxByteCount/ErrorFrameCount` 在每次 flush(33Hz)里 `SetField` → 状态栏 3+ 个 Run 绑定每帧更新。`MainViewModel` 已有 1Hz `_statsTimer`。
**改法**:
- `DataFlowViewModel` 把 flush 循环里的计数累加改为**静默字段累加**(不触发 INPC),新增 `public void NotifyCountsChanged()` 一次性通知 5 个属性
- `MainViewModel._statsTimer.Tick`(已 1Hz)里调用 `_dataFlow.NotifyCountsChanged()`
- Clear 命令(365-366 行)的 `RxCount = 0` 仍走 SetField 立即通知,不受影响

### 改动 3:FlushPendingEntries 每 tick 4 次小分配 → 双并行列表零拷贝
**现状**:`_pendingRx/_pendingTx` 是 `List<(LogEntry,int)>`,swap 时每 tick `new List` ×2 + `new LogEntry[]` ×2,且 AddRange 前要提取数组。
**改法**:
- `_pendingRx/_pendingTx` 改为双并行列表 `List<LogEntry> + List<int>(bytes)`
- flush 时直接把 `List<LogEntry>` 传给 `AddRange`(List 是 IList,命中批量路径,零拷贝),bytes 并行列表仅用于回调循环
- `AddRxEntry/AddTxEntry`(581-599 行)同步改造;公开签名不变
- `ApplyHighlight` 循环改用索引遍历

### 不做(列为后续候选)
- **PaintedCard 阴影重构**(候选 2):DropShadowEffect 包住 ListBox 导致每帧整卡重渲染,但涉及视觉改动需验收,单独一轮做
- **ApplyHighlight 移入队线程**(候选 5):仅当用户配置大量高亮规则时有收益,需快照改造,条件性收益
- 行模板/TextBox 改 TextBlock 等:已核实行内无 Wrap/阴影/TextBox,虚拟化全开,无需动

### 验收标准
1. `dotnet build -c Release`:0 警告 0 错误
2. `dotnet test`:全绿(现有 566 Core + 10 McpServer,行为等价无新测试需求;ScrollPendulum/集合测试已覆盖)
3. 逻辑验证:高帧率 flush 下滚动每帧仅 1 次、计数 1Hz 刷新、flush 零临时分配
4. 工作树干净,commit + push 到 GitHub