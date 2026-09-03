## 第 14 轮:PCAP 导出 UI + Replay 对话框初始目录(两个小补完)

### 现状(已通过代码探测确认)

- `PcapExportService` 后端完整(写 pcap 全局头 + 每包记录,方向前缀字节,TX=0x01/RX=0x02),测试完整(8 个),**但 src/ACCcom 零引用** — DataPanel 导出区只有 TXT/JSON/CSV,没有 PCAP
- `DataFlowViewModel` 已有 `SaveToFile`/`SaveToJson`/`SaveToCsv` 私有方法 + `SaveRx*Command`/`SaveTx*Command` 全套模式,`FileExportService` 已注入
- `ReplayViewModel.ReplayFile()` 的 OpenFileDialog **无 InitialDirectory** — 用户每次要手动导航到 `%LOCALAPPDATA%/ACCcom/recordings` 找录制文件(用 SessionRecorder 录完再回放时尤其烦)

### 实施方案(小而干净,无新窗口)

#### 1. DataFlowViewModel 加 PCAP 导出
- 字段 `PcapExportService _pcapExportService`(与 FileExportService 一样从 MainViewModel 注入,或直接 new — 用 new 更小,但保持一致性选择注入)
- 加 `SaveRxPcapCommand` / `SaveTxPcapCommand`(对应 `SaveToPcap(RxEntries,"RX")` / `SaveToPcap(TxEntries,"TX")`)
- `SaveToPcap` 私有方法:空列表早退 + SaveFileDialog(.pcap,文件名 `ACCCOM_{tag}_{timestamp}.pcap`)+ `_pcapExportService.ExportToPcap(entries, dialog.FileName)` + 状态反馈
- MainViewModel 透传 `SaveRxPcapCommand` / `SaveTxPcapCommand`

#### 2. DataPanel.xaml 加按钮
- RX 导出区 TXT/JSON/CSV 后加 `PCAP` HeaderButton 绑定 `SaveRxPcapCommand`
- TX 导出区同样加 `PCAP` 绑定 `SaveTxPcapCommand`

#### 3. Replay 对话框初始目录
- `ReplayViewModel.ReplayFile()` 的 OpenFileDialog 加 `InitialDirectory = <recordings 目录>`(与 SessionRecorder 默认录制目录一致),目录不存在则跳过
- 顺手:ListRecordings 目录拼逻辑复用(SessionRecorder 有静态 ListRecordings,但初始目录用同一 Path.Combine 即可)

#### 4. i18n — zh-CN.json + en-US.json
- `Button.PCAP` = "PCAP"(两语言同,键存在以防漏)
- `Status.PcapExported` = "已导出 {0} 条记录到 {1}" / "Exported {0} records to {1}"

#### 5. 测试
- Core:现有 PcapExportServiceTests 8 个已覆盖文件格式。补 1 个 `ExportToPcap_InvalidHexString_DoesNotThrow`(脏数据容错,HexToBytes 只处理空格/连字符,`Convert.ToByte` 可能抛 — 确认后补测试或修 service)
- 先验证 `PcapExportService.HexToBytes` 对非 hex 字符的当前行为,决定是修 service(吞异常返回空)还是只补测试

### 不做(避免越界)

- **不做** pcap 文件浏览器/导入
- **不做** 批量导出多会话
- **不做** 其他新窗口

### 验收标准

1. `dotnet build -c Release`:**0 警告 0 错误**
2. `dotnet test`:Core ≥ 490 + MCP 47 = ≥ 537 全绿
3. 手动验证:RX 面板点 PCAP → 存 .pcap → Wireshark 可打开;Replay 对话框默认落在 recordings 目录
4. 工作树干净,1 个 commit