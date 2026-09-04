## 第 53 轮:UI 响应性 — CompareWindow 冻结修复 + 启动路径测量先行

### 背景
上轮探索确认两个候选。本轮做主项 CompareWindow 冻结修复(高收益低风险);启动路径按探索建议"先测量再动刀"——本轮只加计时埋点 + 无风险并行化,`_http.Start()` 移出构造函数、ApplyTheme 异步加载这类中风险改动留到拿到实测数据后的下一轮。

### 改动 1:CompareWindow 对比循环移出 UI 线程 + 逻辑抽入 Core 可测
**现状**:`CompareWindow.xaml.cs:58-73` 逐行对比循环(2 次插值 + 2 次 `new DiffRow` + 2 次 Add × 10 万行)全在 UI 线程 → 秒级冻结。DiffRow 是嵌套在窗口里的 record,零测试。
**改法**:
- 新建 `src/ACCcom.Core/Models/DiffRow.cs`:`public sealed record DiffRow(string Display, bool IsDiff);`(字段不变,XAML 的 `{Binding Display}`/`IsDiff` DataTrigger 无需动)
- 新建 `src/ACCcom.Core/Services/DiffEngine.cs`:`public static (List<DiffRow> RowsA, List<DiffRow> RowsB, int Matching, int Different) BuildDiff(string[] linesA, string[] linesB)`——把 63-73 行逻辑原样搬入(逐行等位对比、空串补位、Ordinal 判等、预分配容量)
- `CompareWindow.Compare_Click`:`await Task.Run(() => DiffEngine.BuildDiff(linesA, linesB))`,解构结果,ItemsSource 赋值与 SummaryText 仍留在 UI 线程(await 后默认回 UI 上下文);新增 catch 兜底 Task.Run 内异常(超大输入等),finally 恢复按钮不变
- `DiffRow` 移入 Core.Models 后 CompareWindow 加 `using ACCcom.Core.Models;`

### 改动 2:新增 DiffEngineTests(5 个)
- 等长全同 / 等长有差异(IsDiff 标记与 matching/different 计数)
- 不等长补位(短文件空串补位,计数正确)
- 空文件对空文件 / 单侧空文件
- 行号前缀格式(`[{i+1}] `)断言
- 大批量输入(如 50k 行)快速完成(冒烟,不卡)

### 改动 3:启动路径计时埋点(零风险,为下一轮提供数据)
- `MainViewModel` 构造函数各阶段加 `Stopwatch` 分段计时:SettingsService.Load / ParserManager / HttpService.Start / 各 ViewModel 构造 / ApplyTheme+BuildThemeOptions / LanguageManager.LoadLanguage,结束时 `Debug.WriteLine("[startup] ctor stages: ...")` 输出各段 ms
- `MainWindow` 构造函数对 `new MainViewModel(...)` 单独计时输出
- 仅添加 Debug 输出,不改任何执行顺序与行为

### 改动 4:InitializeAsync 无风险并行化
- `MainViewModel.InitializeAsync`(L362-368):`LoadShortcutsAsync/LoadPresetsAsync/LoadMacrosAsync` 三连 await → `Task.WhenAll`(互无数据依赖,各自填独立 ObservableCollection);`LoadTriggers` 同步文件读包 `Task.Run`
- 已在 UI 线程外的 fire-and-forget 中执行,并行只省墙钟,无绑定风险

### 不做(留待测量后)
- `_http.Start()` 移出构造函数 / 失败降级:中风险(API/仪表盘就绪时序),下一轮按埋点数据决定
- ApplyTheme 字典异步加载 / BuildThemeOptions 缓存:中风险(主题就位时序),同上
- CompareWindow 行号前缀共享等内存优化:收益中低,冻结修复后无必要

### 验收标准
1. `dotnet build -c Release`:0 警告 0 错误
2. `dotnet test`:全绿(569 Core + 新增 5 = 574,10 McpServer)
3. 逻辑验证:大文件对比 UI 不冻结;启动埋点输出各阶段耗时;InitializeAsync 并行无行为变化
4. 工作树干净,commit + push 到 GitHub