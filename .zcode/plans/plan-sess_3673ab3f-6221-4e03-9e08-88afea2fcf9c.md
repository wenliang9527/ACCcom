## 第 16 轮:状态栏交互补完(双击清空 StatusText + 录制目录打开)

### 现状(已通过代码探测确认)

两个明确的体验死角,都和状态栏相关:

1. **状态栏 StatusText 的 ToolTip 承诺「双击清空」但从未实现**(`StatusBarPanel.xaml:23`)— 状态提示文本会一直停留,没有清空手段。ToolTip 承诺的行为不存在,用户双击无反应,是「功能承诺未兑现」
2. **录制完成后没有「打开录制文件夹」的直达入口** — 回放对话框初始目录上轮已修,但录完想直接用资源管理器查看 .jsonl 文件仍要手动导航到 `%LOCALAPPDATA%/ACCcom/recordings`

已确认的接线模式:
- `MainViewModel.StatusText` 是普通属性(`SetField`),code-behind 可直接 `vm.StatusText = ""`
- `DataFlowViewModel.OpenParserDir()` 已有 `Process.Start("explorer.exe", dir)` 模式,可参照
- `SessionRecorder` 的录制目录常量: `%LOCALAPPDATA%/ACCcom/recordings`(两处重复,可提取共享)

### 实施方案(两处小改,无新窗口)

#### 1. 状态栏 StatusText 双击清空(兑现 ToolTip 承诺)
- `StatusBarPanel.xaml`:给 StatusText TextBlock 加 `MouseLeftButtonUp="OnStatusClearClick"`
- `StatusBarPanel.xaml.cs`:加 `OnStatusClearClick` → `vm.StatusText = ""`(与现有 OnCounterClick/OnRecordingClick 同模式)

#### 2. 录制完成后「打开文件夹」入口
- `MainViewModel`:
  - 加方法 `OpenRecordingsFolder()` — 打开 `%LOCALAPPDATA%/ACCcom/recordings`(目录不存在则创建后打开),状态栏反馈
  - 加命令 `OpenRecordingsFolderCommand` + 透传属性
- `StatusBarPanel.xaml`:
  - REC 指示块旁加一个小「打开文件夹」按钮(仅录制时可见,复用 IsRecording DataTrigger 的可见性)
  - 或:REC 块右键菜单加「打开录制文件夹」项 —— 选右键菜单,更干净不挤占状态栏
- `SessionRecorder`:提取 `RecordingsDirectory` 静态属性,消除两处重复常量,`MainViewModel` 也引用它

#### 3. i18n — zh-CN.json + en-US.json
- `Status.OpenRecordingsFolder` = "已打开录制文件夹" / "Opened recordings folder"
- `Status.RecordingsDirMissing`(可选,目录创建失败时用)
- 状态栏右键菜单项 `Tip.OpenRecordingsFolder` / `Menu.OpenRecordingsFolder`

#### 4. 测试
- `SessionRecorder.RecordingsDirectory` 是纯静态路径属性,补 1 个测试:`RecordingsDirectory_IsUnderLocalAppData`(与 TriggerPathResolver.DataDirectory 测试同款)

### 不做(避免越界)

- **不做**录制文件列表浏览(Replay 对话框已有)
- **不做**状态栏历史/回滚(StatusText 只存最后一条,清空即可)
- **不做**其他状态栏增强

### 验收标准

1. `dotnet build -c Release`:**0 警告 0 错误**
2. `dotnet test`:Core ≥ 495 + MCP 47 = ≥ 542 全绿
3. 手动验证:双击状态栏文本清空;录制中右键 REC 块 → 打开录制文件夹 → 资源管理器弹出
4. 工作树干净,1 个 commit