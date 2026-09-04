## 第 52 轮:数据通路分配与查找收敛

聚焦三个已核实的优化点(CompareWindow 冻结与启动速度留待后续轮):

### 改动 1(零风险·热路径):PatternMatcher 去 ToLowerInvariant 分配
**现状**:`PatternMatcher.MatchesPattern`(src/ACCcom.Core/Services/PatternMatcher.cs:39)每包×每规则调用 `matchMode.ToLowerInvariant()` → 每次分配新字符串。调用链:TriggerService.Evaluate(每包×每启用规则)+ DataBufferWaiter.Matches(有 waiter 时每包)。`ProtocolTestRunner.MatchesExpectation`(:180)同样模式。
**改法**:`matchMode.ToLowerInvariant() switch` → 三个 `Equals(..., StringComparison.OrdinalIgnoreCase)` 分支(contains/exact/regex),零分配。ProtocolTestRunner 同步改(其 exact 分支用 `Ordinal` 区分大小写——语义保留,只换比较方式)。现有 PatternMatcherTests(大小写不敏感用例)守护。

### 改动 2(中风险·读取路径):DataBufferService.GetEntriesSince 全环扫描 → 尾部定位
**现状**:`:91-109` 每次调用从头扫全部 10k 条目过滤 `Id > id`,满环时 10k 次迭代/次调用(MCP read_data、HTTP /api/data 轮询)。
**前提已核实**:LogEntry.Id 由 SerialService/NetworkBridgeService `Interlocked.Increment` 严格单调赋值,新条目总是落在 ring 尾部,`Id > id` 的条目构成连续尾部段。
**改法**:在 `_head` 环形上对 Id 做二分查找下界(线性化索引标准二分,结果模 `_capacity` 映射回环),只拷贝尾部连续段;方向过滤与 limit 合并进单次拷贝。`id >= _maxId` 早退保留。
**测试**:现有 DataBufferServiceTests/ConcurrencyTests 全绿,新增 1 个换行(wrap)场景测试 + 1 个方向过滤测试。

### 改动 3(零风险·热路径):FrameAssembler.Feed 每包 2 次字符串分配 → 单遍追加
**现状**:`:44` `entry.RawHex.Trim()`(分配)+ `:50` `StripSpaces(hex)`(StringBuilder+ToString,分配)→ 每 fragment 包 2 分配 3 遍扫描,才拷入 `_hexBuf`。
**改法**:新增 `AppendHexStripped(string rawHex)` 单遍跳过空白字符直接写 `_hexBuf`(返回有效字符数用于空判断),替换 Trim+StripSpaces+AppendHex 三连;`StripSpaces` 仅保留给 `GetHeaderNoSpace`(低频,缓存后不变)。TryComplete 的 `HexToBytes()` 每完成帧一次 `new byte[]`(最大 4096,非每包)不动。

### 不做
- CompareWindow 大文件 UI 冻结:下一轮做,涉及 Task.Run 重构
- 启动路径首帧延迟:需实测收益,单独一轮
- FrameBuffer.EmitFrame async void / TriggerViewModel 写文件:低频或已可控

### 验收标准
1. `dotnet build -c Release`:0 警告 0 错误
2. `dotnet test`:全绿(567 Core + 10 McpServer,含新增 2 个)
3. 逻辑验证:PatternMatcher 零分配、GetEntriesSince 满环 O(log N+k)、FrameAssembler 单遍追加
4. 工作树干净,commit + push 到 GitHub