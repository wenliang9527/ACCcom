## 第 57 轮:测试假绿修复 + 文档承诺但代码缺失的功能接线

探索确认三个高收益项,全部属于"测试/实现不一致 + 文档有但代码无"主题,风险低收益直接。

### 改动 1:修 SettingsService 测试假绿 + 真实副作用
**现状**:`SettingsServiceTests.DefaultSettingsPath_IsUnderAppBaseDirectory`(:110-121)断言 `AppContext.BaseDirectory/settings.json`,但实现(SettingsService.cs:8-12)实际用 `%LOCALAPPDATA%\ACCcom\settings.json`——**干净环境必挂**,当前靠 bin 残留文件假绿(旧实现 commit 83a4917 用 BaseDirectory,62c4900 改实现没改测试);该测试还写入真实用户配置目录且不清理。另外 `Save()` 的 `Directory.CreateDirectory(BaseDir)`(:63)硬编码 LocalApplicationData,自定义路径实例也会创建真实目录副作用。
**改法**:
- `SettingsService` 加 `public string SettingsPath => _settingsPath;`(暴露实际路径,仿 MacroManagerTests 断言模式)
- 测试改为断言 `SettingsPath.StartsWith(LocalApplicationData)` 且**不写文件**(只验证默认路径归属,消除真实配置副作用)
- `Save()` 的 `Directory.CreateDirectory` 改为 `Path.GetDirectoryName(_settingsPath)`(自定义路径不再碰真实配置目录)
- 删除测试 bin 残留的 settings.json(假绿来源)

### 改动 2:接线 CompareWindow(孤儿窗口)
**现状**:CompareWindow(文件行对比)+ DiffEngine 完整实现且有 8 个测试,但全仓库无 `new CompareWindow()`——docs/guide/visualization.md:22-29 宣称该功能但 UI 打不开。它与 DiffWindow(hex 字节对比)是**不同功能**,DiffEngine 只有它消费。
**改法**:
- `MainViewModel` 加 `OpenCompareCommand` + `OpenCompareWindow()`(懒建守卫 + Closed 置空,仿 OpenVirtualSerialWindow)
- `ToolBarPanel.xaml` 加按钮绑定 `OpenCompareCommand`(放在 Diff 按钮旁);新增语言键 `Button.Compare`/`Tip.CompareFiles`(zh/en)
- `CompareWindow.xaml.cs` 补 `WindowHelper.AttachWindowState(this, "CompareWindow")`(11 个窗口已有,它漏了)
- 修正 `docs/guide/visualization.md` 过期描述(去掉 compare_frames/Modbus/字段级对比 claim,改为实际的文件行对比)

### 改动 3:修 Modbus Auto Poll(文档承诺但 UI 完全失效)
**现状**:`ModbusWindow.xaml:90` Auto Poll CheckBox 绑 `IsChecked="{Binding IsPolling}"`,但 `IsPolling` setter(ModbusViewModel.cs:121)是纯 SetField——**勾选不启动轮询定时器**;`StartPollCommand/StopPollCommand`(:173-174)已实现但零绑定。docs/guide/modbus.md:43-45 宣称"勾选 Auto Poll 按设定周期自动读取"。
**改法**:`IsPolling` setter 改为勾选时调 `StartPoll()`、取消时调 `StopPoll()`(保持命令存在,勾选即生效;StartPoll 内部有 `if (IsPolling) return` 防重入)。行为验证:勾选启动 Timer 轮询 ReadAsync,取消停止。

### 改动 4(顺手清理,低风险)
- `ClearSendHistoryCommand`(DataFlowViewModel:300/400,死代码;发送历史 tooltip 已声称"右键清空"):发送历史上下文菜单加"清空发送历史"项绑定该命令
- `ModbusPriorityQueue`(Core 死服务,仅测试消费):删除文件 + 对应测试

### 不做
- 其余死命令(AddHighlightRuleCommand 透传等):纯冗余无影响,留待后续
- CompareWindow 删除方案:接线成本远低于删除(删除要动窗口+DiffEngine+测试+24 语言键+7 主题资源)

### 验收标准
1. `dotnet build -c Release`:0 警告 0 错误
2. `dotnet test`:全绿(测试数可能因删 ModbusPriorityQueueTests 微降,新增 SettingsService 断言)
3. 逻辑验证:干净环境 SettingsService 测试通过且不写真实配置目录;CompareWindow 按钮可打开;Modbus Auto Poll 勾选即轮询
4. 工作树干净,commit + push 到 GitHub