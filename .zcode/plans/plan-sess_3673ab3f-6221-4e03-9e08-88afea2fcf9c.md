## 第 56 轮:虚拟串口模拟器 + 二级窗口状态持久化与空态提示

用户选择"两个都做"。虚拟串口做**独立模拟器窗口**(方案 A:自持 VirtualSerialService,注入数据显式接入主解析链路,不碰 `_serial` 共享——爆炸半径为零);窗口状态用 **WindowHelper.AttachWindowState 统一方案**(每窗口 1 行)。

### 改动 1:虚拟串口模拟器窗口(功能)
**背景**:`VirtualSerialService`(Core,108 行,完整 ISerialService)有 8 个测试但 UI 零引用;docs/guide/advanced-comms.md:35-43 宣传"无需真实硬件即可测试协议解析器"。
**改法**(完全仿 ProtocolTestWindow 模式):
- 新建 `src/ACCcom/ViewModels/VirtualSerialViewModel.cs`:自持 `VirtualSerialService`;命令 OpenCommand/CloseCommand/InjectRxCommand/SendCommand/ClearCommand;`ObservableCollection<LogEntry>` 记录窗口内 TX/RX;构造参数含 `DataFlowViewModel`——注入 RX 时同步调 `_dataFlow.OnSerialData(entry)` 接入 FrameBuffer/解析器/高亮/RxEntries 全链路(兑现文档承诺)
- 新建 `src/ACCcom/VirtualSerialWindow.xaml(.cs)`:仿 ProtocolTestWindow(自绘标题栏 + SetupTitleBar);布局:连接控制(端口名/波特率 + Open/Close)、RX 注入框(hex 输入 + 注入按钮)、TX 发送框、TX/RX 滚动日志
- `MainViewModel`:加 `OpenVirtualSerialCommand` + `OpenVirtualSerialWindow()`(懒建 VM、单例守卫、Closed 时 Dispose,复刻 `OpenProtocolTestWindow` MainViewModel.cs:477-503)
- `ToolBarPanel.xaml` 加按钮绑定 `OpenVirtualSerialCommand`
- 语言键(zh/en):`Button.VirtualSerial`/`Tip.OpenVirtualSerial`/窗口内各标签
- VirtualSerialService 无需改(Core 已完整)

### 改动 2:二级窗口位置/大小持久化(打磨)
**背景**:仅 MainWindow 保存窗口状态;13 个二级窗口都不持久化;`_settings`/`_settingsService` 在 MainViewModel 私有,二级窗口无访问渠道。
**改法**:
- `AppSettings` 加 `public record WindowRect(double X, double Y, double Width, double Height);` + `public Dictionary<string, WindowRect> WindowStates { get; set; } = new();`(与现有 FieldGridColumnWidths 字典风格一致;System.Text.Json 缺失属性取默认,旧 settings.json 零破坏)
- `WindowHelper` 加:`SetSettingsProvider(Func<AppSettings>)`(MainWindow ctor 设一次)+ `AttachWindowState(Window, string key)`——订阅 Loaded(恢复)+ Closed(保存);恢复时用 `SystemParameters.VirtualScreen` 做屏幕内相交校验,窗口跑到屏外时回退居中(顺带回补 MainWindow 现有直接赋值)
- 12 个二级窗口(跳过孤儿 CompareWindow)ctor 各加 1 行 `WindowHelper.AttachWindowState(this, "XxxWindow");`(ReplayWindow/FrameAssemblerConfigWindow 为 NoResize,只存位置)
- `MainViewModel.SaveSettings` 末尾把 `WindowStates` 一并写入 settings;MainWindow.OnClosed 前 provider 已就绪
- 不持久化 WindowState(最大化状态),避免与 SetupTitleBar 双击最大化交互

### 改动 3:列表空状态提示(打磨)
**背景**:全仓库无 EmptyTemplate;DataGrid/ListBox 空时全是空白。
**改法**:
- App.xaml 加共享 `EmptyListTextBlock` 样式(居中、InkSecondaryBrush、FontSize 12)
- 给 7 个最常用的列表加空态提示(DataTrigger on Items.Count==0 显示 TextBlock):RX/TX ListBox(DataPanel)、宏列表(MacroWindow)、触发器规则(TriggerWindow)、高亮规则(HighlightWindow)、Modbus 扫描结果与寄存器(ModbusWindow)、协议测试步骤(ProtocolTestWindow)
- 语言键(zh/en):`Common.EmptyList`("暂无数据"/"No data")

### 测试
- Core 侧:VirtualSerialService 已有 8 测试全绿;AppSettings 新字段不影响现有 SettingsServiceTests(JSON 缺省兼容)
- 新增 1 个测试:`SettingsService` roundtrip 含 WindowStates 字典(存 2 个窗口位置 → Load → 断言还原)
- UI 侧(窗口/持久化)按仓库惯例不做自动测试,靠构建 + 逻辑验证

### 验收标准
1. `dotnet build -c Release`:0 警告 0 错误
2. `dotnet test`:全绿(576 Core + 新增 1 = 577,10 McpServer)
3. 逻辑验证:虚拟串口窗口可注入 RX 并出现在主 RX 列表;二级窗口移动/缩放后重启恢复位置;空列表显示"暂无数据"
4. 工作树干净,commit + push 到 GitHub