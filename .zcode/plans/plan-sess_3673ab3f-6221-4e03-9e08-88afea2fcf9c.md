## 第 55 轮:前端功能增强 — 死代码接线 + UX 安全网

探索确认:无 TODO/占位/主题漂移,真正的金矿是"Core/VM/窗口已完整实现但 UI 零入口"的死代码(3 处)与半成品功能。本轮全部为低风险接线/增强,不做中风险的虚拟串口与低收益的窗口状态恢复。

### 改动 1:会话回放入口(死代码接线,最高性价比)
**现状**:`ReplayFileCommand`(MainViewModel:651 → ReplayViewModel:26)完整实现 + `ReplayWindow.xaml` 完整窗口,但**全仓库零绑定**——用户无法从桌面 UI 打开回放;docs/guide/automation.md 却已宣传该功能。
**改法**:`ToolBarPanel.xaml` 录制按钮旁加"回放"按钮绑定 `ReplayFileCommand`(Segoe MDL2 `&#xE768;` Play);新增语言键 `Button.Replay`/`Tip.Replay`(zh/en 同步)。

### 改动 2:协议可视化编辑器入口(死代码接线)
**现状**:`OpenSchemaEditorCommand`(MainViewModel:294,655,723)实现完整(ShowDialog SchemaEditorWindow),零调用;`Button.SchemaEditor`/`Tip.SchemaEditor` 语言键已存在(zh:46,128);docs 宣传"离线解析"依赖此窗口。
**改法**:`ToolBarPanel.xaml` 加"编辑器"按钮绑定 `OpenSchemaEditorCommand`(MDL2 `&#xE70F;` Edit);en-US 补 `Button.SchemaEditor`/`Tip.SchemaEditor`(确认是否已存在,不存在则补)。

### 改动 3:书签列表 + 删除(半成品补全)
**现状**:`RemoveBookmarkCommand`(BookmarkViewModel:34)已实现但零绑定;`Bookmarks` 集合零 XAML 绑定——书签只能加/上下跳,看不到列表、删不掉。
**改法**:
- `BookmarkViewModel` 新增 `JumpToBookmarkCommand`(按 EntryId 在 Rx/TxEntries 中定位条目并选中,复用 BookmarkManager 语义)+ `RemoveBookmark` 已有
- `ToolBarPanel.xaml` 书签按钮组加"书签列表"下拉(ItemsSource=Bookmarks,每项显示 Label+Preview+Direction,点击跳转;项内含删除命令 RemoveBookmarkCommand,CommandParameter=BookmarkItem)
- 新增语言键(zh/en):`Button.BookmarkList`/`Tip.BookmarkList`/`Menu.DeleteBookmark`

### 改动 4:破坏性操作确认(UX 安全网)
**现状**:全工程 MessageBox 出现 0 次——清空 RX/TX(Ctrl+L 直清)、删除宏/预设/触发器全部直接执行无确认。
**改法**:新建 `ConfirmDialog.xaml`(仿 PromptDialog 风格:标题+消息+确认/取消,主题一致,~40 行);接入:
- `DataFlowViewModel.ClearRxCommand/ClearTxCommand`(:373-374)清空前确认
- `MacroViewModel.DeleteMacro`、`PresetViewModel.DeletePreset`、`TriggerViewModel.DeleteTrigger` 删除前确认
- 新增语言键(zh/en):`Confirm.ClearRx`/`Confirm.ClearTx`/`Confirm.DeleteMacro`/`Confirm.DeletePreset`/`Confirm.DeleteTrigger`/`Confirm.Title`(或复用 Title 键)

### 改动 5:RX/TX 右键菜单增强(前端)
**现状**:DataPanel.xaml:107-111/318-322 右键菜单只有"复制选中";DataFlowViewModel 已有 SaveRxCommand/SaveRxCsvCommand/SaveRxJsonCommand/SaveRxPcapCommand/ClearRxCommand/RxFilterText(184)全未接菜单。
**改法**:RX 菜单加:复制 Hex(选中项 RawHex 到剪贴板,code-behind)、另存为 TXT(绑定 SaveRxCommand)、清空 RX(绑定 ClearRxCommand);TX 菜单对应(SaveTxCommand/ClearTxCommand)。新增语言键 `Button.CopyHex`/`Menu.SaveAsTxt`(zh/en)。

### 不做
- 虚拟串口 UI(候选 3):`_serial` 实例被 SerialController/Modbus/NetworkBridge 共享,替换需架构级处理,中风险,单独轮
- 二级窗口位置持久化/空状态提示(候选 8):收益低,后续轮
- MetricsCollector 进 StatsWindow(候选 7):低收益

### 验收标准
1. `dotnet build -c Release`:0 警告 0 错误
2. `dotnet test`:全绿(576 Core + 10 McpServer,无新增测试点——全部为 UI 接线)
3. 逻辑验证:回放/编辑器按钮可打开窗口;书签列表可见可跳转可删除;清空/删除有确认;右键菜单新项可用
4. 工作树干净,commit + push 到 GitHub