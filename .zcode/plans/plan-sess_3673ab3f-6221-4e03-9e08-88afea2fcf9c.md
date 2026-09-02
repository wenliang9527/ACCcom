## 第 10 轮:AutoBaudDetector UI(自动波特率探测)

### 现状(已通过代码探测确认)

- `src/ACCcom.Core/Services/AutoBaudDetector.cs` 后端完整:`DetectAsync(portName, ct)` 按常见波特率(9600, 115200, ... 1200)依次试探,返回命中的波特率或 0
- `MainViewModel.cs:196` 已实例化 `new AutoBaudDetector()` 但只传给 HttpService,**WPF UI 完全无入口** — 用户拿到陌生设备仍得手动猜波特率
- `AutoBaudDetector` 已有完整测试(`AutoBaudDetectorTests.cs`)
- 连接面板布局清晰:`ConnectionPanel.xaml` 串口模式的 BaudRate ComboBox 是加按钮的最佳位置

### 实施方案(零干扰、无对话框)

#### 1. ConnectionViewModel 增加 AutoDetectBaudCommand
- 构造接收 `AutoBaudDetector` 引用(可选参数,保持向后兼容)
- 加 `AutoDetectBaudCommand` (RelayCommand,CanExecute 检查 `!IsDetecting && !IsOpen && !string.IsNullOrEmpty(SelectedPort)`)
- 加属性 `bool IsDetecting` + `string DetectProgressText`(例如 "9600…" → "115200…")
- `DetectAsync` 方法:
  - 用 CancellationTokenSource 支持取消
  - 对每个 baud 调 `TryBaudRateAsync`,Update `DetectProgressText` 反映进度
  - 命中第一个即返回;扫完无果返回 0
  - 成功:设 `SelectedBaudRate = detected`,StatusText 反馈
  - 失败:StatusText 提示"未检测到响应"
- 用 `async RelayCommand` 不阻塞 UI

#### 2. MainViewModel 注入 AutoBaudDetector 到 ConnectionViewModel
- 把 `_autoBaudDetector` 字段(当前传给 HttpService 的)抽出来,同时传给 `ConnectionViewModel`
- 或者:ConnectionViewModel 直接 `new AutoBaudDetector()` — 但为可测试性,从外部注入更好

#### 3. ConnectionPanel.xaml 加按钮
- 在 BaudRate ComboBox 右侧加一个 magic-wand 按钮 `&#x269B;` (原子轨迹图标) 或 `&#x2699;`(齿轮,但已用于 Advanced)
- 选用 `&#x269B;` 避免冲突 — 或更直观的 `&#x1F50D;`(放大镜)
- 按钮绑定 `AutoDetectBaudCommand`,显示 `DetectProgressText` tooltip
- 按钮在非 Serial 模式自动 Collapsed(随父 StackPanel Visibility)

#### 4. MainWindow.xaml.cs 无需改(命令直接走 ViewModel 绑定)
- 不加热键(避免占用,且非高频操作)

#### 5. i18n — zh-CN.json + en-US.json
- `Tip.AutoDetectBaud`: "自动探测波特率(打开端口前点击)"
- `Status.BaudDetecting`: "正在探测波特率:{0} baud…"
- `Status.BaudDetected`: "已检测到波特率:{0}(请打开端口验证)"
- `Status.BaudDetectFailed`: "未检测到设备响应,请确认端口已连接并上电"
- `Status.BaudDetectCanceled`: "已取消波特率探测"

#### 6. 测试
- `AutoBaudDetectorTests.cs` 已存在 — 只补 1 个 `Dispose_is_idempotent` 边界测试(若已有则跳过)
- 不做 WPF 集成测试

### 不做(避免越界)

- **不做**对话框(连接面板内联按钮更顺手)
- **不做**进度条控件(文字状态 + 按钮禁用已够)
- **不做**自定义波特率列表编辑(超出本轮)
- **不做**Esc 取消(按钮点击期间再次点击 = 取消,自然语义)

### 验收标准

1. `dotnet build -c Release`:**0 警告 0 错误**
2. `dotnet test`:Core ≥ 476 + MCP 47 = ≥ 523 全绿
3. 手动验证:选 Serial 模式 → 选端口(未打开)→ 点 ⚡ AutoDetect → 状态栏滚动显示 baud → 命中后 SelectedBaudRate 自动更新
4. 工作树干净,1 个 commit