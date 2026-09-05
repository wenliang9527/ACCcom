## 第 54 轮:启动路径优化 — 主题加载缓存 + HTTP 延迟启动 + 埋点可见化

第 53 轮承诺"拿到实测数据后处理启动路径"。探索已核实三项优化的安全性,本轮实施。主题 XAML 是编译进程序集的资源(运行期不可变),缓存 100% 安全;MainWindow 对主题资源 100% DynamicResource,ApplyTheme 延迟不会 XamlParseException。

### 改动 1(低风险·确定性收益):ThemeManager 字典缓存 — 消灭 7 次主题 XAML 解析
**现状**:`BuildThemeOptions`(MainViewModel ctor + 语言切换两条路径)对每个主题调 `GetAccent` → 每次 `new ResourceDictionary { Source = pack://.../Themes/{id}.xaml }` **完整解析整个主题 XAML**(共 7 次);`ApplyTheme`(App.xaml.cs:126)切换主题时再解析 1 次。
**改法**(`ThemeManager.cs`):
- 新增 `static readonly Dictionary<string, ResourceDictionary>` + lock,`GetDictionary(string fileName)` 按需加载并缓存(失败不缓存,保留 catch→Gray 语义)
- `GetAccent` 改从 `GetDictionary` 取字典再查 `Accent` key(不再自己 new 字典)
- `App.ApplyTheme` 改用 `ThemeManager.GetDictionary(fileName)` 替代 `new ResourceDictionary{Source=uri}`(缓存实例的 Source 仍含 `Themes/`,purge 循环与 Insert(0) 顺序语义不变;`_activeThemeId` 短路守卫保持)
- 收益:启动时 7 次完整解析 → 首次 1 次(首次访问某主题时才解析);主题切换从"每次重新解析"变"零解析"

### 改动 2(中风险·行为变化):_http.Start() 移出构造函数 — 失败降级不再崩
**现状**:MainViewModel ctor:227 同步 `_http.Start()`;端口被占(8899)时抛 InvalidOperationException → MainWindow ctor 崩 → 启动失败弹框。已核实无任何代码假设"窗口显示时 HTTP 已监听"(MCP 代理已删;Modbus dashboard 是用户触发型,有 catch)。
**改法**:
- `MainViewModel` 新增 `public void StartHttpAsync()`(或 `async Task`):try/catch 包 `_http.Start()`,失败时 `StatusText` 提示而非抛异常(消息走语言资源或直接中文提示,与现有 StatusText 用法一致)
- `MainWindow` 构造末尾(或 Loaded 事件)触发:`Loaded += ...` 里调用(异步、不阻塞首帧)
- 退出竞态:Start 在飞时 `Dispose`(OnClosed → _vm.Dispose → _http.Dispose)——StartAsync 内 catch 兜底即可,EmbedIO Dispose 安全
- HttpService 不新增 IsRunning 属性(状态栏 HttpUrl 是常量显示,无就绪依赖;OpenDashboard 未起时浏览器显示连接拒绝,可接受)

### 改动 3(零风险·支撑):启动埋点 Debug → Trace,Release 可见
**现状**:第 53 轮埋点用 `Debug.WriteLine`——`[Conditional("DEBUG")]` 在 Release 构建**整行不编译**,Release 下无任何输出(用户实际跑 Release exe,测量无效)。
**改法**:MainViewModel ctor 的 `Stage` helper 与 MainWindow 的 ctor 计时改 `Trace.WriteLine`(Release 默认定义 TRACE,输出可被 DebugView/VS 附加查看;不写文件,保持轻量)。分段不变(settings+parser / http start / viewmodels / theme / language / total)。

### 不做
- ApplyTheme 延迟到 Loaded:非 Light 用户首帧闪变,收益(省 1 次解析)已被改动 1 覆盖
- SettingsService.Load / _highlights.Load / LoadLanguage 异步化:小文件 ~ms 级,复杂度不值
- CompareWindow 行号前缀共享等:冻结已修复,无必要

### 测试
- 改动均在 WPF 工程(ACCcom),Core.Tests/McpServer.Tests 不引用 WPF 工程,无新增测试点;现有 576 Core + 10 McpServer 必须全绿
- 逻辑验证:构建 0 警告 0 错误(TreatWarningsAsErrors);主题缓存语义(首次解析、切换零解析、失败回退 Gray);HTTP 启动失败降级为状态栏提示;Release 构建下 [startup] 埋点 Trace 输出存在

### 验收标准
1. `dotnet build -c Release`:0 警告 0 错误
2. `dotnet test`:全绿(576 Core + 10 McpServer)
3. 逻辑验证:主题缓存生效(GetDictionary 命中)、_http.Start 失败不崩、Trace 埋点 Release 可见
4. 工作树干净,commit + push 到 GitHub