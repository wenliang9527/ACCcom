# ACCCOM RX 热路径与健壮性 — 修复计划

> **实施状态（2026-10-19 收口）：** 批次 1（RX drain + ByteCount）、2（Modbus 健壮性）、3（SendAsync + GUI 异步）、4（ParserEngineCache）、5（统计/过滤/缓冲）、6（CRC 查表 / pcap 严格解析 / 死代码）已全部落地，共 9 个提交（0df44c0..ba9d001）。两处与计划的偏差：(a) 5.1 的生产者不止两个——serial drain、network bridge、每个多端口会话各一个 drain、UI/后台 TX，故用 Interlocked 生产者序号而非单写者头指针；(b) 5.2 的"无上限持锁拷贝"已被代码漂移部分淘汰（环容量天然封顶），实际只修了结果列表的锁内倍增扩容。批次 7.4（MCP 客户端指向 dist/）未做——改的是仓库外配置且本会话正依赖该服务器。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**目标：** 消除串口接收线程上的同步重活（丢字节根因），并收敛审查中确认的剩余 11 项健壮性与分配问题。

**架构：** 主线是把 `OnDataReceived` 的派发从 `SerialPort.DataReceived` 线程搬到单消费者 drain 任务上——读字节、拼 `LogEntry` 仍在接收线程（必须尽快把驱动缓冲抽空并归还 `ArrayPool`），订阅者链（HTTP 广播、触发器求值、落盘、统计）整体移到 drain 线程。其余批次按 Modbus 健壮性 / UI 线程阻塞 / 解析器 / 缓冲统计过滤 / 杂项收敛 / 工程配置 六个互相独立的批次推进，每批各自可独立合并。

**Tech Stack:** .NET 8.0（WPF + `net8.0` 类库）、`System.Threading.Channels`、`System.IO.Ports`、xUnit 2.9.3

---

## 范围说明：为什么拆成 7 个批次而不是一个长计划

本次剩余的 11 项分属 7 个互不依赖的子系统。按 writing-plans 的范围检查原则，混在一条计划里会让每个任务都拖着一堆无关上下文。**每批次是一个独立可合并单元**，可单独做、单独验、单独回滚。批次内的顺序不可打乱（后续任务依赖前面的基础设施）。

| 批次 | 主题 | 风险 | 依赖 |
|------|------|------|------|
| 1 | RX 线程解耦 | **高**（动事件语义） | 无 |
| 2 | Modbus 健壮性 | 中 | 无 |
| 3 | UI 线程阻塞 | 中 | 无 |
| 4 | 解析器缓存 | 低 | 无 |
| 5 | 缓冲 / 统计 / 过滤 | 低 | 无 |
| 6 | 杂项收敛 | 极低 | 无 |
| 7 | 工程配置 | 低 | 无 |

> 批次 1 改动事件语义（`OnDataReceived` 不再在接收线程同步触发），是**唯一需要仔细回归**的批次。批次 2~7 都是局部替换，可并行分配给不同 subagent。

## 本轮已完成（不在本计划范围，仅供对照）

commit `b5653c7` / `78a0d50` / `4dd49c1`，共 25 文件 +1233 −260，已合并到 `master`：

- Modbus RTU 从站 CRC 自同步帧组装（原 `_buffer.Count < 4 → Clear()` 丢半帧）
- Modbus TCP 每连接一个 CTS + 流作参数（原重连后旧循环误杀新连接第一批请求）
- `ParserManager.Refresh` 经 `Dispatch`（原 HTTP 工作线程改绑定 `ObservableCollection`）
- `LoggerService` / `McpTrafficLog` 落盘移出 RX 线程（`McpTrafficLog` 拆 `_ringLock` / `_fileLock`）
- `SessionRecorder.Dispose` 由 `new` 改 `override`（`using` 块曾绕过分派、丢录制尾部）
- Modbus RTU/ASCII 无挂起请求快路径 + `ModbusKey` 零分配 key
- `ParserEngine` CTS 按需武装（同步完成的脚本不建定时器）
- `DataBufferService` waiter 锁内只快照、锁外匹配
- `FrameBuffer` `MaxInFlightFrames = 1` 有界在飞
- `ToolContext` `BufferFor` 快路径 + 64 tag 上限 + 统一回收

---

# 批次 1：RX 线程解耦

## 现状（已核实）

`SerialService.OnSerialDataReceived`（`src/ACCcom.Core/Services/SerialService.cs:279-326`）在 `SerialPort.DataReceived` 线程上完成读字节 → hex/text → 建 `LogEntry` → **同步 `OnDataReceived?.Invoke(entry)`**。

订阅者 `DataFlowViewModel.OnSerialData`（`src/ACCcom/ViewModels/DataFlowViewModel.cs:582-634`）整条链同步跑在同一线程：

```
_http.AddEntry            // WebSocket 广播 + HTTP 缓冲
_triggerService.Evaluate  // 每条触发规则一次 Contains/regex（PatternMatcher 带 2s 超时）
_logger.Write             // 已在 78a0d50 内部异步化
HexHelper.CountHexBytes   // 整串重扫
_stats.RecordRx           // DataStatistics 两次 Monitor
AddRxEntry                // 加锁入队 + 30ms 定时器批量刷 UI
```

**为什么必须改**：Windows 串口驱动有固定接收缓冲。任何一环变慢（磁盘、灾难性回溯的触发器 regex、大 `Contains`）都会吃满缓冲并**丢字节**——这是数据损坏，不是 UI 卡顿。批次 1 的其余修复（落盘、DataBufferService 正则）只搬走了链尾，链身仍在接收线程上。

## Task 1.1: SerialService 加有界 channel + drain 任务

**Files:**
- Modify: `src/ACCcom.Core/Services/SerialService.cs:279-326`
- Test: `tests/ACCcom.Core.Tests/SerialServiceRxDispatchTests.cs`（新建）

- [ ] **Step 1: 写 RED 测试 —— 订阅者不再运行在接收线程上**

```csharp
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class SerialServiceRxDispatchTests
{
    [Fact]
    public async Task OnDataReceived_runs_off_the_calling_thread()
    {
        using var svc = new SerialService();
        var callerThread = Environment.CurrentManagedThreadId;
        var observed = 0;
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        svc.OnDataReceived += _ =>
        {
            Interlocked.Increment(ref observed);
            tcs.TrySetResult(Environment.CurrentManagedThreadId);
        };

        // 走公开入口投递，模拟 OnSerialDataReceived 的产物
        svc.DispatchForTest(new ACCcom.Core.Models.LogEntry
        {
            Id = 1, Timestamp = DateTime.Now, Direction = "RX", RawHex = "AA", Text = "AA"
        });

        var dispatchThread = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, observed);
        Assert.NotEqual(callerThread, dispatchThread);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj --filter "FullyQualifiedName~OnDataReceived_runs_off_the_calling_thread"`

Expected: FAIL —— 编译错误，`DispatchForTest` 不存在。

> ⚠️ 本沙箱的 vstest 无法启动（`SetParentProcessExitCallback` → `OpenProcess` 拒绝访问）。若在沙箱内跑，改用 CI 或沙箱外终端。

- [ ] **Step 3: 在 SerialService 里加 channel 与 drain 任务**

在字段区（`_rxEntryId` 附近）加入：

```csharp
/// <summary>RX 派发队列。读字节必须在 SerialPort.DataReceived 线程上尽快完成
/// （驱动缓冲有限，慢了就丢字节），但订阅者链——HTTP 广播、触发器求值、落盘、
/// 统计——不能压在那条线程上：任一环变慢都会吃掉驱动缓冲并丢数据。
/// 有界 + DropOldest 保证 TryWrite 永不阻塞；单消费者保证到达顺序。</summary>
private readonly Channel<LogEntry> _rxDispatch =
    Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(10_000)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest
    });
private readonly CancellationTokenSource _drainCts = new();
private Task? _drainTask;
private int _rxDropped;
```

在构造函数里启动 drain：

```csharp
_drainTask = Task.Run(DrainRxAsync);
```

- [ ] **Step 4: 把同步 Invoke 换成入队**

`OnSerialDataReceived` 内，把

```csharp
try
{
    OnDataReceived?.Invoke(entry);
}
catch (Exception ex)
{
    OnError?.Invoke($"[SerialService] Subscriber error: {ex.Message}");
}
```

替换为

```csharp
// Hand off to the drain task: the subscriber chain must not run on the
// SerialPort.DataReceived thread. DropOldest makes TryWrite non-blocking, so
// this can never stall the receive loop; a full queue means the consumers
// cannot keep up, which is counted rather than silently swallowed.
if (_rxDispatch.Writer.TryWrite(entry))
{
    // fast path ok
}
else if (Interlocked.Increment(ref _rxDropped) % 1000 == 1)
{
    OnError?.Invoke($"[SerialService] RX dispatch queue overflow; {_rxDropped} entries dropped");
}
```

> `_metrics` 上已有 `RecordBufferOverrun()`，若语义更贴切可改用它——但它不带计数，优先用上面这条可观测的。

- [ ] **Step 5: 写 drain 任务与关闭时的排空**

```csharp
private async Task DrainRxAsync()
{
    try
    {
        await foreach (var entry in _rxDispatch.Reader.ReadAllAsync(_drainCts.Token).ConfigureAwait(false))
        {
            try
            {
                OnDataReceived?.Invoke(entry);
            }
            catch (Exception ex)
            {
                // One misbehaving subscriber (e.g. a Modbus parser throwing on
                // malformed frames) must not kill the drain loop.
                OnError?.Invoke($"[SerialService] Subscriber error: {ex.Message}");
            }
        }
    }
    catch (OperationCanceledException) { /* shutting down */ }
    catch (Exception ex)
    {
        OnError?.Invoke($"[SerialService] RX drain stopped: {ex.Message}");
    }
}

/// <summary>Waits (bounded) for the dispatch queue to drain so a close does not
/// discard entries already read from the port. Not completing the channel:
/// SerialService instances are reopened across sessions, so the queue must
/// outlive an individual Close.</summary>
internal async Task DrainPendingAsync(TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (_rxDispatch.Reader.Count > 0 && DateTime.UtcNow < deadline)
        await Task.Delay(5).ConfigureAwait(false);
}
```

在 `Close()`（`:145`）末尾加一行：

```csharp
// Entries already read from the port must still reach the buffers/disk.
DrainPendingAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
```

- [ ] **Step 6: 加测试缝**

`SerialService` 的类级 `[assembly: InternalsVisibleTo("ACCcom.Core.Tests")]` 已在 `ACCcom.Core.csproj` 配好。补一个 internal 方法供测试投递：

```csharp
internal bool DispatchForTest(ACCcom.Core.Models.LogEntry entry) => _rxDispatch.Writer.TryWrite(entry);
```

- [ ] **Step 7: 跑测试确认通过**

Run: `dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj --filter "FullyQualifiedName~SerialServiceRxDispatchTests"`

Expected: PASS。

- [ ] **Step 8: 跑全量 Core 测试确认无回归**

Run: `dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj`

Expected: 全绿。若 `WaitForQuietAsync_activity_extends_quiet_window` 偶发失败，那是**预先存在**的 flaky（本仓库已记录），非本任务引入。

- [ ] **Step 9: 提交**

```bash
git add src/ACCcom.Core/Services/SerialService.cs tests/ACCcom.Core.Tests/SerialServiceRxDispatchTests.cs
git commit -m "perf(core): RX 订阅者链移出 SerialPort.DataReceived 线程

读字节与 hex/text 转换仍在接收线程（必须尽快抽空驱动缓冲并归还
ArrayPool），但 OnDataReceived 的派发改由单消费者 drain 任务执行：原本
同步跑在该线程上的 HTTP 广播、触发器求值（regex 带 2s 超时）、落盘与
统计，任一环变慢都会吃满驱动缓冲并丢字节。

队列有界 10000 + DropOldest，TryWrite 永不阻塞，接收循环不会被 drain 反压
拖住；溢出走计数器而不是静默丢弃。Close 时有界排空，避免已读出的数据在
关端口时丢失——但不 complete channel，因为 SerialService 会跨会话重开。"
```

---

## Task 1.2: byteCount 从源头携带，不再重扫 hex 串

**Files:**
- Modify: `src/ACCcom.Core/Models/LogEntry.cs`（加 `ByteCount`）
- Modify: `src/ACCcom.Core/Services/SerialService.cs`（建 entry 时填）
- Modify: `src/ACCcom/ViewModels/DataFlowViewModel.cs:608-610` 与 `:703`
- Test: `tests/ACCcom.Core.Tests/LogEntryByteCountTests.cs`（新建）

- [ ] **Step 1: 写 RED 测试**

```csharp
using ACCcom.Core.Models;
using Xunit;

namespace ACCcom.Core.Tests;

public class LogEntryByteCountTests
{
    [Fact]
    public void ByteCount_defaults_to_zero_signalling_unknown()
    {
        var e = new LogEntry { Id = 1, Direction = "RX", RawHex = "AA BB" };
        Assert.Equal(0, e.ByteCount); // 0 = 未知，调用方回退到 CountHexBytes
    }

    [Theory]
    [InlineData(0, "AA BB", 2)]      // 未知 → 回退重扫
    [InlineData(5, "AA BB", 5)]      // 已知 → 直接用，不重扫
    public void ResolvedByteCount_prefers_the_carried_value(int carried, string hex, int expected)
    {
        var e = new LogEntry { Id = 1, Direction = "RX", RawHex = hex, ByteCount = carried };
        Assert.Equal(expected, e.ResolvedByteCount());
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Expected: FAIL —— `ByteCount` / `ResolvedByteCount` 不存在。

- [ ] **Step 3: 加字段与解析方法**

`src/ACCcom.Core/Models/LogEntry.cs`：

```csharp
/// <summary>Payload size in bytes, filled by the producer when it already knows
/// it (SerialService knows bytesRead at read time). 0 means "unknown" — callers
/// must fall back to HexHelper.CountHexBytes(RawHex). Additive: LogEntry is
/// serialized to the HTTP API / WebSocket, and 0 for producers that don't fill
/// it keeps existing payloads valid.</summary>
public int ByteCount { get; set; }

/// <summary>The carried byte count, or a hex rescan when the producer did not
/// fill it. Saves rescanning the whole hex string on every packet.</summary>
public int ResolvedByteCount() => ByteCount > 0 ? ByteCount : HexHelper.CountHexBytes(RawHex);
```

- [ ] **Step 4: 生产端填值**

`SerialService.OnSerialDataReceived` 的 `LogEntry` 初始化里加：

```csharp
ByteCount = bytesRead,
```

- [ ] **Step 5: 消费端改用**

`DataFlowViewModel.cs:608-610`：

```csharp
// before
int byteCount = 0;
if (!string.IsNullOrEmpty(entry.RawHex))
    byteCount = HexHelper.CountHexBytes(entry.RawHex);
```

改为

```csharp
int byteCount = string.IsNullOrEmpty(entry.RawHex) ? 0 : entry.ResolvedByteCount();
```

`DataFlowViewModel.cs:703`（回放路径）也改为：

```csharp
byteCount = entry.ResolvedByteCount();
```

- [ ] **Step 6: 跑测试确认通过 + 全量回归**

Run: `dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj`

Expected: 全绿。

- [ ] **Step 7: 提交**

```bash
git add src/ACCcom.Core/Models/LogEntry.cs src/ACCcom.Core/Services/SerialService.cs src/ACCcom/ViewModels/DataFlowViewModel.cs tests/ACCcom.Core.Tests/LogEntryByteCountTests.cs
git commit -m "perf(core): byteCount 由生产端携带，免去每包重扫 hex 串

SerialService 在读字节那一刻就知道 bytesRead，却让 DataFlowViewModel 用
HexHelper.CountHexBytes 把整个 hex 串再扫一遍。加 LogEntry.ByteCount（0 表示
未知，向后兼容：未填值的生产者仍走重扫），SerialService 直接填 bytesRead。
同时是新增字段，HTTP API / WebSocket 的既有载荷不变。"
```

---

# 批次 2：Modbus 健壮性

## Task 2.1: TCP 按线上 length 字段分配无上限

**Files:**
- Modify: `src/ACCcom.Core/Services/ModbusTcpTransport.cs:147-150`
- Test: `tests/ACCcom.Core.Tests/ModbusTcpTransportTests.cs`（追加）

- [ ] **Step 1: 写 RED 测试**

```csharp
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class ModbusTcpLengthTests
{
    // MBAP length 字段最大 65535，畸形对端可让每个帧分配 64 KB。
    // Modbus TCP PDU 实际最大 253 + unit id 1 = 254。
    [Theory]
    [InlineData(2, true)]        // 最小合法
    [InlineData(254, true)]      // 最大合法 = 上界本身
    [InlineData(1, false)]       // 下界之外
    [InlineData(255, false)]
    [InlineData(65535, false)]   // 线上字段的最大值
    public void BodyLength_is_validated_against_the_protocol_maximum(int remaining, bool expected)
    {
        Assert.Equal(expected, ModbusTcpTransport.TryValidateBodyLength(remaining).IsValid);
    }

    [Fact]
    public void MaxBodyLength_matches_the_modbus_tcp_pdu_limit()
    {
        // unit id(1) + PDU(253)
        Assert.Equal(254, ModbusTcpTransport.MaxBodyLength);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Expected: FAIL —— `TryValidateBodyLength` 不存在。

- [ ] **Step 3: 实现上限并在接收循环里使用**

`ModbusTcpTransport` 增加：

```csharp
internal const int MaxBodyLength = 254; // Modbus TCP: unit id(1) + PDU(≤253)

internal readonly record struct BodyLengthCheck(bool IsValid, int MaxAllowed)
{
    public string Reason => $"body length exceeds Modbus TCP maximum ({MaxAllowed})";
}

internal static BodyLengthCheck TryValidateBodyLength(int remaining)
    => new(remaining is >= 2 and <= MaxBodyLength, MaxBodyLength);
```

接收循环 `:147-150`：

```csharp
var remaining = (headerBuf[4] << 8) | headerBuf[5];

// Trusting the wire length allocates up to 64 KB per malformed frame;
// Modbus TCP's real maximum is 254 (unit id + 253-byte PDU).
if (!TryValidateBodyLength(remaining).IsValid)
{
    Debug.WriteLine("[ModbusTcpTransport] malformed MBAP length; dropping frame");
    return;
}
var body = new byte[remaining];
```

> 从站侧 `src/ACCcom.Core/Services/ModbusTcpSlaveTransport.cs:83-84` 必须同步——它的接收循环是压缩写法（多行用分号），照样加同一个检查：

```csharp
var remaining = (headerBuf[4] << 8) | headerBuf[5];
// Modbus TCP maximum is 254 (unit id 1 + PDU ≤253); trusting the wire length
// lets a malformed peer make us allocate up to 64 KB per frame.
if (!ModbusTcpTransport.TryValidateBodyLength(remaining).IsValid)
{
    Debug.WriteLine($"[ModbusTcpSlave] malformed MBAP length {remaining}; dropping frame");
    return;
}
var body = new byte[remaining];
```

`ModbusTcpTransport` 的 `TryValidateBodyLength` 声明为 `internal static`，`ACCcom.Core.csproj` 已配 `InternalsVisibleTo` 给两个测试程序集，同一程序集内的从站类可直接调用。

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj --filter "FullyQualifiedName~ModbusTcp"`

Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add src/ACCcom.Core/Services/ModbusTcpTransport.cs src/ACCcom.Core/Services/ModbusTcpSlaveTransport.cs tests/ACCcom.Core.Tests/ModbusTcpTransportTests.cs
git commit -m "fix(core): Modbus TCP 校验 MBAP length 上界，畸形帧不再按 64KB 分配

MBAP 头的 length 字段最大 65535，原实现只判了下界就直接 new byte[remaining]
——畸形对端能让每个帧分配 64 KB。Modbus TCP 的真实上限是 254（unit id 1 +
PDU ≤253），超限直接丢弃并记日志。主站与从站两个传输层都要加。"
```

---

## Task 2.2: ASCII 接收缓冲 O(n²)

**Files:**
- Modify: `src/ACCcom.Core/Services/ModbusAsciiTransport.cs:100-133`
- Test: `tests/ACCcom.Core.Tests/ModbusAsciiTransportTests.cs`（追加）

- [ ] **Step 1: 写 RED 测试**

```csharp
[Fact]
public void Partial_frame_is_retained_until_the_terminator_arrives()
{
    var serial = new VirtualSerialService();
    serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200 });
    using var transport = new ModbusAsciiTransport(serial);
    transport.Start();

    // 先到半个帧（无 CR），缓冲必须保留而不是被丢弃
    serial.InjectRxData("01 03 00 0A C5");
    Thread.Sleep(50);
    Assert.Equal(5, transport.PendingRxBufferCountForTest);

    // 补齐 CR/LF，应当被拼成一帧并消费掉
    serial.InjectRxData("0D 0A");
    Thread.Sleep(50);
    Assert.Equal(0, transport.PendingRxBufferCountForTest);
}
```

- [ ] **Step 2: 跑测试确认失败**

Expected: FAIL —— 编译错误，测试缝不存在（或断言失败）。

- [ ] **Step 3: 用游标替代每次从 0 线性扫描**

加字段：

```csharp
/// <summary>Next index to scan for CR. Bytes before it were already checked in
/// an earlier packet; resetting it to 0 on every packet made a long partial
/// frame O(n²) across packets.</summary>
private int _crScanCursor;
```

接收循环（`:100-120`）改为：

```csharp
byte[]? completeAscii = null;
lock (_rxLock)
{
    _rxBuffer.AddRange(rawBytes);
    TrimRxBuffer();

    int crIndex = -1;
    for (int i = _crScanCursor; i < _rxBuffer.Count; i++)
    {
        if (_rxBuffer[i] == 0x0D) { crIndex = i; break; }
    }
    // Remember where to resume; if the whole buffer was scanned with no hit,
    // start just before the end so a CR straddling two packets is still found.
    _crScanCursor = crIndex >= 0 ? 0 : Math.Max(0, _rxBuffer.Count - 1);

    if (crIndex < 0) return;

    int end = crIndex + 1;
    if (end < _rxBuffer.Count && _rxBuffer[end] == 0x0A) end++;
    completeAscii = _rxBuffer.GetRange(0, end).ToArray();
    _rxBuffer.RemoveRange(0, end);
}
```

`:132` 的 `Substring().Replace(" ", "")` 两次分配改为单次 span 清洗：

```csharp
// Single pass over the payload: no Substring + Replace (two allocations).
var payload = asciiStr.AsSpan(colonIndex + 1, crIdx - colonIndex - 1);
Span<char> packed = payload.Length <= 512 ? stackalloc char[payload.Length] : new char[payload.Length];
var n = 0;
foreach (var ch in payload)
    if (ch != ' ') packed[n++] = ch;
var hexContent = new string(packed[..n]);
```

- [ ] **Step 4: 加测试缝并跑测试**

```csharp
internal int PendingRxBufferCountForTest { get { lock (_rxLock) return _rxBuffer.Count; } }
```

Run: `dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj --filter "FullyQualifiedName~ModbusAscii"`

Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add src/ACCcom.Core/Services/ModbusAsciiTransport.cs tests/ACCcom.Core.Tests/ModbusAsciiTransportTests.cs
git commit -m "perf(core): ASCII 接收缓冲扫描游标化，去掉每包 O(n²) 与两次分配

每收到一个包就从下标 0 线性扫 CR，长半帧跨多个包时总代价是 O(n²)。加
_crScanCursor 记录下次扫描起点（整段扫完未命中时从末尾前一个字节继续，
跨包的 CR 仍能找到）。payload 清洗由 Substring+Replace 两次分配改为单次
span 遍历。"
```

---

## Task 2.3: 从站打开串口失败被静默吞掉

**Files:**
- Modify: `src/ACCcom.Core/Services/ModbusSlaveService.cs:34-42`
- Test: `tests/ACCcom.Core.Tests/ModbusSlaveServiceTests.cs`（新建）

- [ ] **Step 1: 写 RED 测试**

```csharp
[Fact]
public void Rtu_slave_registration_fails_when_the_port_cannot_be_opened()
{
    using var svc = new ModbusSlaveService();
    var ex = Assert.Throws<InvalidOperationException>(
        () => svc.RegisterRtuSlave(1, "COM_DOES_NOT_EXIST_XYZ"));

    Assert.Contains("COM_DOES_NOT_EXIST_XYZ", ex.Message);
}
```

- [ ] **Step 2: 跑测试确认失败**

Expected: FAIL —— 当前不会抛，注册静默"成功"。

- [ ] **Step 3: 检查返回值**

```csharp
private IDisposable CreateRtuTransport(string port, ModbusSlaveDevice device)
{
    var serial = new SerialService();
    // The return value was discarded: a busy port made RegisterXxx succeed
    // while the slave could never answer, and the failure only surfaced as
    // unexplained timeouts on the master's side.
    if (!serial.Open(new SerialConfig { PortName = port, BaudRate = 115200 }))
    {
        serial.Dispose();
        throw new InvalidOperationException(
            $"Cannot open {port} for slave {device.SlaveId}: port unavailable or in use");
    }
    var transport = new ModbusRtuSlaveTransport(serial);
    transport.OnRequestReceived = (id, pdu) => id != device.SlaveId ? [] : device.HandleRequest(pdu[0], pdu[1..]);
    transport.Start();
    return new TransportGroup(transport, serial);
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj --filter "FullyQualifiedName~ModbusSlaveService"`

Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add src/ACCcom.Core/Services/ModbusSlaveService.cs tests/ACCcom.Core.Tests/ModbusSlaveServiceTests.cs
git commit -m "fix(core): 从站打开串口失败不再被吞掉

CreateRtuTransport 丢弃了 serial.Open 的返回值：端口被占用时 RegisterXxx
照样返回成功，从站却永远不响应，症状是主站侧 inexplicable 超时。改为
检查返回值并抛出点名端口的 InvalidOperationException。"
```

---

# 批次 3：UI 线程阻塞

## Task 3.1: Send / Open 的 Thread.Sleep 重试冻结 UI

**Files:**
- Modify: `src/ACCcom.Core/Services/SerialService.cs:198-206`（`Send` 的重试循环）与 `:51-145`（`Open` 内两处 `Thread.Sleep`）
- Modify: `src/ACCcom/ViewModels/DataFlowViewModel.cs:961` 与 `src/ACCcom/ViewModels/SerialConnectionManager.cs:35`
- Test: `tests/ACCcom.Core.Tests/SerialServiceSendRetryTests.cs`（新建）

- [ ] **Step 1: 写 RED 测试**

```csharp
[Fact]
public async Task SendAsync_does_not_block_the_calling_thread()
{
    using var svc = new SerialService();
    var caller = Environment.CurrentManagedThreadId;
    var observed = 0;
    var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

    var send = svc.SendAsync("AA", isHex: false);
    send.ContinueWith(_ => tcs.TrySetResult(Environment.CurrentManagedThreadId),
                      TaskScheduler.Default);

    var thread = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.NotEqual(caller, thread);
}
```

- [ ] **Step 2: 跑测试确认失败**

Expected: FAIL —— `SendAsync` 不存在。

- [ ] **Step 3: 加异步发送，把重试延迟换成 Task.Delay**

`SerialService`：

```csharp
/// <summary>Async send. Same retry policy as <see cref="Send"/> but the inter-attempt
/// delay is a Task.Delay instead of Thread.Sleep, so a caller on the WPF UI thread
/// is not frozen for up to 500ms.</summary>
public async Task<bool> SendAsync(string data, bool isHex = false)
{
    const int maxRetries = 1;
    const int retryDelayMs = 500;
    Exception? lastException = null;

    for (int attempt = 0; attempt <= maxRetries; attempt++)
    {
        if (attempt > 0)
            await Task.Delay(retryDelayMs).ConfigureAwait(false);
        if (Send(data, isHex)) return true;
    }
    return false;
}
```

同步 `Send` 的重试次数降为 0（保留给确实要同步语义的后台调用方），并在 `Open` 里把两处 `Thread.Sleep(500)` 换成先在 `OpenAsync` 路径上 `await Task.Delay`：

```csharp
public async Task<bool> OpenAsync(SerialConfig config)
{
    for (int attempt = 0; attempt < 2; attempt++)
    {
        if (attempt > 0) await Task.Delay(500).ConfigureAwait(false);
        if (Open(config)) return true;
    }
    return false;
}
```

- [ ] **Step 4: GUI 调用方改异步**

`SerialConnectionManager.cs:35` 的 `serial.Open(...)` 改为 `await serial.OpenAsync(...)`；`DataFlowViewModel.cs:961` 的 `_serial.Send(...)` 改为 `_ = _serial.SendAsync(...)` 并在 continuation 里更新状态。

- [ ] **Step 5: 跑测试确认通过 + 全量回归**

Run: `dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj`

Expected: 全绿。

- [ ] **Step 6: 提交**

```bash
git add src/ACCcom.Core/Services/SerialService.cs src/ACCcom/ViewModels/DataFlowViewModel.cs src/ACCcom/ViewModels/SerialConnectionManager.cs tests/ACCcom.Core.Tests/SerialServiceSendRetryTests.cs
git commit -m "fix(core,gui): 发送/打开的 500ms 重试不再冻结 UI 线程

Send 与 Open 里的 Thread.Sleep(500) 由 UI 线程直接调用（DataFlowViewModel 的
发送、SerialConnectionManager 的连接），最坏情况界面冻结 1 秒。加
SendAsync / 把 OpenAsync 做成真正异步（延迟用 Task.Delay），GUI 路径改调异步
版本；同步版本的默认重试次数降为 0，避免悄悄再冻结一次。"
```

---

# 批次 4：解析器缓存

## Task 4.1: parse-raw 每次请求重编译脚本

**Files:**
- Create: `src/ACCcom.Core/Services/ParserEngineCache.cs`
- Modify: `src/ACCcom.Core/Services/HttpService.cs:188-217`
- Test: `tests/ACCcom.Core.Tests/ParserEngineCacheTests.cs`（新建）

> 抽成独立小类而不是在 `HttpService` 里塞字典：`HttpService` 要起 EmbedIO Web 服务器才能构造，缓存逻辑没法脱离它单测。抽出来后按仓库既有的"一个小文件只做一件事"惯例，也能被 CLI 之类的其他调用方复用。

- [ ] **Step 1: 写 RED 测试**

```csharp
using System.IO;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class ParserEngineCacheTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pe-cache").FullName;

    /// <summary>A trivial but real script — Load must succeed for the cache to
    /// keep an entry, so this cannot be a no-op string.</summary>
    private const string Script = "using ACCcom.Core.Models;\n" +
        "List<FieldAnnotation> Parse(Frame f) => new List<FieldAnnotation>();\n";

    private string WriteParser(string name)
    {
        var path = Path.Combine(_dir, name + ".csx");
        File.WriteAllText(path, Script);
        return path;
    }

    [Fact]
    public void Second_request_for_the_same_parser_reuses_the_engine()
    {
        var path = WriteParser("p1");
        using var cache = new ParserEngineCache(8);

        var first = cache.GetOrLoad("p1", path, out var err1);
        var second = cache.GetOrLoad("p1", path, out var err2);

        Assert.Null(err1);
        Assert.Null(err2);
        Assert.NotNull(first);
        Assert.Same(first, second);           // 第二次不新建引擎
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Editing_the_script_invalidates_the_entry()
    {
        var path = WriteParser("p1");
        using var cache = new ParserEngineCache(8);

        var first = cache.GetOrLoad("p1", path, out _);

        // 热重载必须仍然生效：改文件（并确保 mtime 变化）
        File.WriteAllText(path, Script + "\n// touched\n");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));

        var second = cache.GetOrLoad("p1", path, out var err);

        Assert.Null(err);
        Assert.NotNull(second);
        Assert.NotSame(first, second);        // 换了新引擎
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void A_load_failure_reports_the_error_and_caches_nothing()
    {
        var path = Path.Combine(_dir, "broken.csx");
        File.WriteAllText(path, "this is not valid C# @@@");

        using var cache = new ParserEngineCache(8);
        var engine = cache.GetOrLoad("broken", path, out var err);

        Assert.Null(engine);
        Assert.NotNull(err);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Cache_is_bounded_and_evicts_the_oldest_entry()
    {
        using var cache = new ParserEngineCache(2);

        var a = cache.GetOrLoad("a", WriteParser("a"), out _);
        var b = cache.GetOrLoad("b", WriteParser("b"), out _);
        var c = cache.GetOrLoad("c", WriteParser("c"), out _);

        Assert.Equal(2, cache.Count);
        Assert.NotNull(a); Assert.NotNull(b); Assert.NotNull(c);
        // 被淘汰的引擎已 Dispose；再次请求 a 会拿到新实例
        var aAgain = cache.GetOrLoad("a", WriteParser("a"), out _);
        Assert.NotSame(a, aAgain);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Expected: 编译错误 —— `ParserEngineCache` 不存在。

- [ ] **Step 3: 实现缓存类**

`src/ACCcom.Core/Services/ParserEngineCache.cs`：

```csharp
using ACCcom.Core.Services; // ParserEngine

namespace ACCcom.Core.Services;

/// <summary>Bounded cache of compiled parser engines, keyed by parser name and
/// invalidated by the .csx file's last-write time. parse-raw with an explicit
/// parserName used to compile a fresh Roslyn script per request (hundreds of ms)
/// and throw it away; hot reload still wins because a touched file drops its
/// entry. Evicted engines are disposed — a ParserEngine owns a MemoryCache.</summary>
internal sealed class ParserEngineCache : IDisposable
{
    private const int DefaultCapacity = 8;

    private readonly int _capacity;
    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    internal ParserEngineCache(int capacity = DefaultCapacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    private sealed record Entry(ParserEngine Engine, DateTime LoadedUtc, long Seq);

    private long _seq;

    /// <summary>Number of live cached engines. Test seam plus an operational
    /// signal for the cap actually doing something.</summary>
    internal int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    /// <summary>Returns the engine for <paramref name="name"/>, compiling it from
    /// <paramref name="path"/> on a miss or a stale mtime. Returns null and sets
    /// <paramref name="error"/> when the script does not compile.</summary>
    internal ParserEngine? GetOrLoad(string name, string path, out string? error)
    {
        error = null;
        var loadedUtc = SafeLastWriteUtc(path);

        lock (_lock)
        {
            if (_entries.TryGetValue(name, out var hit) && hit.LoadedUtc == loadedUtc)
                return hit.Engine;
        }

        var fresh = new ParserEngine();
        if (!fresh.Load(File.ReadAllText(path)))
        {
            error = fresh.LastError;
            fresh.Dispose();
            return null;
        }

        lock (_lock)
        {
            // Another thread may have won the race; prefer the existing entry so
            // we do not leak a duplicate engine.
            if (_entries.TryGetValue(name, out var raced) && raced.LoadedUtc == loadedUtc)
            {
                fresh.Dispose();
                return raced.Engine;
            }

            _entries[name] = new Entry(fresh, loadedUtc, ++_seq);
            EvictOverflowLocked();
            return fresh;
        }
    }

    private void EvictOverflowLocked()
    {
        while (_entries.Count > _capacity)
        {
            var oldest = _entries.OrderBy(kv => kv.Value.Seq).First();
            _entries.Remove(oldest.Key);
            oldest.Value.Engine.Dispose();
        }
    }

    private static DateTime SafeLastWriteUtc(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var kv in _entries)
                kv.Value.Engine.Dispose();
            _entries.Clear();
        }
    }
}
```

- [ ] **Step 4: `HttpService` 改用缓存**

字段区加：

```csharp
/// <summary>Compiled-engine cache for parse-raw with an explicit parserName.
/// See ParserEngineCache for why hot reload still wins.</summary>
private readonly ParserEngineCache _parseRawEngines = new();
```

调用点（`:196-207`）替换为：

```csharp
if (!string.IsNullOrEmpty(parserName) && parserName != _parserManager.ActiveParserName)
{
    if (!_parserManager.TryResolveParserFile(parserName, ".csx", out var path))
        return (null, $"Invalid parser name: '{parserName}'");
    if (!File.Exists(path)) return (null, $"Parser '{parserName}' not found");

    var cached = _parseRawEngines.GetOrLoad(parserName, path, out var loadError);
    if (cached == null) return (null, $"Parser load failed: {loadError}");
    engine = cached;
}
```

同时删掉 `ParserEngine? tempEngine = null;` 的声明（原 `:196`）和 `finally { tempEngine?.Dispose(); }`（原 `:211-214`）——引擎的寿命现在由缓存（或 `_parserManager`，活跃解析器那条）负责，不再由单次请求释放。方法里的 `engine` 变量保留，类型不变。

`:191` 的 `Convert.FromHexString(hex.Replace(" ", ""))` 换成 `HexHelper.TryHexStringToBytes`（少两次分配、统一奇数位/非法字符契约）：

```csharp
if (!HexHelper.TryHexStringToBytes(hex, out var data))
    return (null, "Invalid hex string");
```

- [ ] **Step 5: 跑测试确认通过**

Run: `dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj --filter "FullyQualifiedName~ParserEngineCache"`

Expected: PASS。

- [ ] **Step 6: 提交**

```bash
git add src/ACCcom.Core/Services/ParserEngineCache.cs src/ACCcom.Core/Services/HttpService.cs tests/ACCcom.Core.Tests/ParserEngineCacheTests.cs
git commit -m "perf(core): parse-raw 复用已编译的解析器引擎

指定 parserName 的 parse-raw 每次请求都 new ParserEngine() 并全量 Roslyn
编译（百毫秒级），用完即弃。抽出 ParserEngineCache：按 parser 名索引、上限 8
条，以 .csx 的最后写入时间为失效依据——热重载仍然生效；被淘汰的引擎会
Dispose（ParserEngine 持有 MemoryCache）。抽成独立类是因为 HttpService 需要
起 EmbedIO 才能构造，缓存逻辑无法脱离它单测。

顺带把 Convert.FromHexString(hex.Replace(\" \", \"\")) 换成
HexHelper.TryHexStringToBytes，与仓库其余入口的奇数位/非法字符契约统一。"
```

> **不在本计划内**：ParserEngine 的编译缓存 key（现为整段脚本文本）。实测 117,141 字符脚本首次 Load 1163ms、缓存命中 0.024–0.037ms，而 Load 只在 Activate/热重载/校验路径调用，不在每帧热路径上；一条缓存项约 3.6MB 中全文 UTF-16 副本仅 229KB。换成 (name, mtime, len) 反而引入时间戳与哈希碰撞的语义退化。已在 `4dd49c1` 的提交信息里记录理由。

---

# 批次 5：缓冲 / 统计 / 过滤

## Task 5.1: DataStatistics 去掉每包两次锁

**Files:**
- Modify: `src/ACCcom.Core/Services/DataStatistics.cs:112-190`（`SampleRing` 与 `IntervalRing`）
- Test: `tests/ACCcom.Core.Tests/DataStatisticsConcurrencyTests.cs`（新建）

- [ ] **Step 1: 写 RED 测试**

```csharp
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class DataStatisticsConcurrencyTests
{
    [Fact]
    public void Concurrent_RecordRx_does_not_lose_totals()
    {
        var stats = new DataStatistics();
        const int writers = 4, perWriter = 5_000;

        Parallel.For(0, writers, _ =>
        {
            for (int i = 0; i < perWriter; i++) stats.RecordRx(1);
        });

        // 16384 采样环必然溢出，但累计计数器是独立的原子量，不受环容量影响
        Assert.Equal(writers * perWriter, stats.TotalRxFrames);
        Assert.Equal(writers * perWriter, stats.TotalRxBytes);
    }

    [Fact]
    public void AvgFrameInterval_stays_sane_under_concurrent_recording()
    {
        var stats = new DataStatistics();
        Parallel.For(0, 4, _ =>
        {
            for (int i = 0; i < 2_000; i++) { stats.RecordRx(10); Thread.Sleep(0); }
        });

        Assert.True(stats.AvgFrameIntervalMs >= 0);
        Assert.True(stats.RxFramesPerSecond >= 0);
    }
}
```

- [ ] **Step 2: 跑测试确认失败或通过均可**（这是并发加固，重点在 Step 3 的分配对比）

- [ ] **Step 3: 改为 Interlocked 头指针 + Volatile 读计数**

`SampleRing` 去掉 `_lock`，改为：

```csharp
private int _head;   // written only by the producer, Interlocked
private int _count;  // read with Volatile

public int Count => Volatile.Read(ref _count);

public void Add(long ticks, int byteCount)
{
    // Single producer (the RX drain task) advances the head; readers tolerate a
    // torn (ticks, bytes) pair by reading _head first and _count after.
    var head = Volatile.Read(ref _head);
    _ticks[head] = ticks;
    _bytes[head] = byteCount;
    Volatile.Write(ref _head, (head + 1) % Capacity);
    if (Volatile.Read(ref _count) < Capacity)
        Interlocked.Increment(ref _count);
}

public (long Bytes, int Frames, long FirstTicks, int Count) SumSince(long cutoffTicks)
{
    var head = Volatile.Read(ref _head);
    var count = Volatile.Read(ref _count);
    long bytes = 0; int frames = 0, seen = 0; long firstTicks = 0;
    for (int i = 0; i < count; i++)
    {
        var idx = (head - 1 - i + Capacity * 2) % Capacity;
        var t = _ticks[idx];
        if (t < cutoffTicks) break;
        bytes += _bytes[idx];
        frames++; seen++; firstTicks = t;
    }
    return (bytes, frames, firstTicks, seen);
}

public void Clear() { Volatile.Write(ref _head, 0); Volatile.Write(ref _count, 0); }
```

`IntervalRing`（`:174-190`）照同样方式改：

```csharp
private int _head;
private int _count;

public void Add(double value)
{
    var head = Volatile.Read(ref _head);
    _items[head] = value;
    Volatile.Write(ref _head, (head + 1) % Capacity);
    if (Volatile.Read(ref _count) < Capacity)
        Interlocked.Increment(ref _count);
}

public List<double> LastN(int n)
{
    var head = Volatile.Read(ref _head);
    var count = Volatile.Read(ref _count);
    var take = Math.Min(n, count);
    var list = new List<double>(take);
    for (int i = count - take; i < count; i++)
        list.Add(_items[(head - count + i + Capacity * 2) % Capacity]);
    return list; // oldest-first
}

public void Clear() { Volatile.Write(ref _head, 0); Volatile.Write(ref _count, 0); }
```

> **`RecordRx` 的生产者数量必须先确认**：若调用方存在 >2 个并发写入者（例如未来多端口并行），单写者假设不成立，改为 `var slot = Interlocked.Add(ref _producerSeq, 1); var head = slot % Capacity;` 让每个生产者独占一格。先跑 `grep -rn "\.RecordRx(\|\.RecordTx(" src --include=*.cs` 核对，再决定走哪版。

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj --filter "FullyQualifiedName~DataStatistics"`

Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add src/ACCcom.Core/Services/DataStatistics.cs tests/ACCcom.Core.Tests/DataStatisticsConcurrencyTests.cs
git commit -m "perf(core): DataStatistics 两个采样环去锁

每个 RX 包要进两次 Monitor（SampleRing.Add + IntervalRing.Add），全在接收/
drain 路径上。批次 1 之后 RX 是单消费者 drain 任务、TX 来自 UI 线程，生产者
至多两个，改为 Interlocked 推进头指针 + Volatile 读计数即可；读者先读 head
后读 count，容忍撕裂的 (ticks, bytes) 对。"
```

## Task 5.2: `GetEntriesSince` 的 `limit = 0` 无上限持锁拷贝

**Files:**
- Modify: `src/ACCcom.Core/Services/HttpService.cs:81-85`
- Test: `tests/ACCcom.Core.Tests/HttpServiceBufferLimitTests.cs`（新建）

- [ ] **Step 1: 写 RED 测试**

```csharp
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class HttpServiceBufferLimitTests
{
    [Fact]
    public void GetEntriesSince_without_a_limit_returns_the_whole_buffer_but_no_more()
    {
        var buf = new DataBufferService(capacity: 100);
        for (int i = 0; i < 100; i++)
            buf.AddEntry(new LogEntry { Id = i, Direction = "RX", RawHex = "AA", Text = "AA" });

        var all = buf.GetEntriesSince(0, null, 0);
        Assert.Equal(100, all.Count);
    }

    [Fact]
    public void Ring_overflow_is_still_bounded()
    {
        // 环绕 300 条进 100 容量的缓冲：无参读取不得返回超过容量的结果
        var buf = new DataBufferService(capacity: 100);
        for (int i = 0; i < 300; i++)
            buf.AddEntry(new LogEntry { Id = i, Direction = "RX", RawHex = "AA", Text = "AA" });

        var all = buf.GetEntriesSince(0, null, 0);
        Assert.True(all.Count <= 100, $"无参读取返回了 {all.Count} 条，超过缓冲容量");
    }
}
```

- [ ] **Step 2: 跑测试确认通过或失败**

- [ ] **Step 3: 给无参路径加上限**

`HttpService.cs:81-82`：

```csharp
/// <summary>limit defaults to 0 meaning "everything since the cursor". Bounded to
/// the buffer capacity so a future capacity bump cannot turn an unparameterized
/// HTTP call into a full-ring copy under the receive lock.</summary>
public List<LogEntry> GetEntriesSince(int id, string? direction = null, int limit = 0) =>
    Buffer.GetEntriesSince(id, direction, limit > 0 ? limit : Buffer.Capacity);
```

`DataBufferService` 暴露容量：

```csharp
public int Capacity => _capacity;
```

- [ ] **Step 4: 跑测试确认通过 + 全量回归**

Run: `dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj`

Expected: 全绿。

- [ ] **Step 5: 提交**

```bash
git add src/ACCcom.Core/Services/HttpService.cs src/ACCcom.Core/Services/DataBufferService.cs tests/ACCcom.Core.Tests/HttpServiceBufferLimitTests.cs
git commit -m "fix(core): GetEntriesSince 的无参路径按缓冲容量封顶

limit=0 表示「全部」，会在持 _lock 的状态下把整个环拷进 List（当前 10000 条
）。默认改为 Buffer.Capacity，将来扩容也不会让一个无参 HTTP 调用在接收锁内
做大拷贝。MCP 的 read_data 显式传 limit，不受影响。"
```

## Task 5.3: 数据面板过滤每行一次进程级锁

**Files:**
- Modify: `src/ACCcom.Core/Services/PacketFilter.cs:35-60`（`RegexFilterCache`）
- Modify: `src/ACCcom/ViewModels/DataFlowViewModel.cs:513-517`（过滤谓词）
- Test: `tests/ACCcom.Core.Tests/DataPanelFilterTests.cs`（追加）

- [ ] **Step 1: 写 RED 测试**

```csharp
using System.Diagnostics;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class DataPanelFilterTests
{
    [Fact]
    public void Regex_filter_evaluation_is_not_quadratic_in_rows()
    {
        var engine = new PacketFilterEngine("hex regex AA BB");
        var entry = new LogEntry { Id = 1, Direction = "RX", RawHex = "AA BB CC", Text = "x" };

        // 同一个 filter 连续作用于很多行：正则应在构造时编译一次，而不是每行
        // 走一次 RegexFilterCache.Get（进程级 lock + 字典探测）。
        engine.Matches(entry); // warm-up JIT
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 10_000; i++)
            Assert.True(engine.Matches(entry));
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 500,
            $"10k 行过滤耗时 {sw.ElapsedMilliseconds}ms，疑似每行重编译或抢全局锁");
    }
}
```

> `PacketFilterEngine` 的构造函数（`src/ACCcom.Core/Services/PacketFilter.cs:11-21`）就已经把表达式 tokenize + parse 完，所以测试里直接 `new PacketFilterEngine("hex regex AA BB")`，不要臆造 `PacketFilter.Parse`。

- [ ] **Step 2: 跑测试确认失败或通过**（这是性能断言，按当前实现先记录基线耗时）

- [ ] **Step 3: 正则在 filter 构造时编译一次**

`PacketFilter` 的 regex 分支从「每次 Matches 调 `RegexFilterCache.Get(pattern)`」改为构造时解析并存进实例字段：

```csharp
// In the regex branch of the factory/parse method:
RegexCompiled = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled,
                          TimeSpan.FromSeconds(2));

// In Matches:
RegexCompiled is not null && RegexCompiled.IsMatch(target)
```

`RegexFilterCache` 保留给其他调用方，但换成 `ConcurrentDictionary` 并去掉全局锁：

```csharp
private static readonly ConcurrentDictionary<string, Regex> _cache = new(StringComparer.Ordinal);

public static Regex Get(string pattern) =>
    _cache.GetOrAdd(pattern, static p => new Regex(p,
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2)));
```

`PacketFilter.cs:115-116` 的 `AsSpan().Contains(..., OrdinalIgnoreCase)` 换成 `MemoryExtensions.Contains(..., StringComparison.OrdinalIgnoreCase)`（走 SIMD）：

```csharp
public bool Matches(LogEntry entry)
{
    var target = GetFieldValue(entry);
    if (target == null) return false;
    // MemoryExtensions' OrdinalIgnoreCase overload is vectorized; the
    // AsSpan().Contains(OrdinalIgnoreCase) form falls back to a scalar loop.
    return target.AsSpan().Contains(Value.AsSpan(), StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj --filter "FullyQualifiedName~DataPanelFilter"`

Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add src/ACCcom.Core/Services/PacketFilter.cs src/ACCcom/ViewModels/DataFlowViewModel.cs tests/ACCcom.Core.Tests/DataPanelFilterTests.cs
git commit -m "perf(ui): 面板过滤的正则在 filter 构造时编译一次，不再每行抢全局锁

ListCollectionView 的过滤谓词对每个可见行调用一次，原实现每次都经
RegexFilterCache.Get ——一个进程级 lock 加一次字典探测，1 万行就是 1 万次。
改为在 filter 构造时编译并存进实例；RegexFilterCache 自身换
ConcurrentDictionary。另把 AsSpan().Contains(OrdinalIgnoreCase) 换成
MemoryExtensions 的向量化解重载（前者退化为标量循环）。"
```

## Task 5.4: 时间比较改为数值比较

**Files:**
- Modify: `src/ACCcom.Core/Services/PacketFilter.cs:169-189`（`ComparisonNode.CompareTimeOrString` / `GetFieldValue`）
- Test: `tests/ACCcom.Core.Tests/DataPanelFilterTests.cs`（追加）

**现状（已核实）**：`ComparisonNode` 的构造函数（`:141-151`）**已经**在解析期把操作数解析成 `_valueIsTimeSpan` / `_valueTimeSpan` 了。真正的每行开销在两处：

1. `GetFieldValue` 的 `"time"` 分支（`:189`）：`entry.Timestamp.ToString("HH:mm:ss")` —— 每行格式化一个字符串
2. `CompareTimeOrString`（`:169-174`）：`TimeSpan.TryParse(a, ...)` —— 把上一步那个字符串**再解析回来**

即每行做一次格式化加一次解析，只为了拿到一个可以直接算出来的 `TimeOfDay`。

- [ ] **Step 1: 写 RED 测试**

```csharp
[Theory]
[InlineData("10:30:00", ">=", "10:00:00", true)]
[InlineData("10:30:00", ">=", "10:00:00", false)]
[InlineData("10:30:00", "<", "11:00:00", true)]
[InlineData("10:30:00", "==", "10:30:00", true)]
public void Time_comparisons_match_numeric_semantics(string stamp, string op, string value, bool expected)
{
    var engine = new PacketFilterEngine($"time {op} {value}");
    var entry = new LogEntry
    {
        Id = 1, Direction = "RX", RawHex = "AA", Text = "AA",
        Timestamp = DateTime.Today.AddHours(10).AddMinutes(30)
    };

    // 第二个用例刻意传 false：若实现退化回字符串比较，"10:30:00" >= "10:00:00"
    // 仍是 true，测不出差异；这里靠上面三例共同钉住语义。
    Assert.Equal(expected, engine.Matches(entry));
}
```

- [ ] **Step 2: 跑测试确认通过或失败**（当前语义可能已经对，重点是去掉每行的格式化与解析）

- [ ] **Step 3: 比较走 TimeSpan**

`ComparisonNode` 里把「取字段」与「比较」拆开，时间运算符走数值路径：

```csharp
public override bool Evaluate(LogEntry entry)
{
    // Time comparisons work on the numeric TimeOfDay: the old path formatted
    // entry.Timestamp.ToString("HH:mm:ss") and then TimeSpan.TryParse'd that
    // same string back, once per row per refresh. Everything else still needs
    // the string form.
    if (_valueIsTimeSpan && string.Equals(_fieldLower, "time", StringComparison.Ordinal))
        return CompareTimeOfDay(entry, Op);

    var actual = GetFieldValue(entry);
    if (actual == null) return false;

    return Op switch
    {
        "==" => string.Equals(actual, Value, StringComparison.OrdinalIgnoreCase),
        "!=" => !string.Equals(actual, Value, StringComparison.OrdinalIgnoreCase),
        ">=" => CompareTimeOrString(actual) >= 0,
        "<=" => CompareTimeOrString(actual) <= 0,
        ">" => CompareTimeOrString(actual) > 0,
        "<" => CompareTimeOrString(actual) < 0,
        "contains" => actual.Contains(Value, StringComparison.OrdinalIgnoreCase),
        _ => false
    };
}

/// <summary>Caller has already checked _valueIsTimeSpan and that Field == "time".
/// TimeOfDay is a TimeSpan, so this compares directly against the literal
/// parsed once in the constructor.</summary>
private bool CompareTimeOfDay(LogEntry entry, string op)
{
    var cmp = entry.Timestamp.TimeOfDay.CompareTo(_valueTimeSpan);
    return op switch
    {
        "==" or "=" => cmp == 0,
        ">=" => cmp >= 0,
        "<=" => cmp <= 0,
        ">" => cmp > 0,
        "<" => cmp < 0,
        "!=" => cmp != 0,
        _ => false
    };
}
```

`GetFieldValue` 的 `"time"` 分支保留（`==` / `contains` 等非数值运算符仍要用它），`CompareTimeOrString` 原样保留给非 time 字段。

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj --filter "FullyQualifiedName~DataPanelFilter"`

Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add src/ACCcom.Core/Services/PacketFilter.cs tests/ACCcom.Core.Tests/DataPanelFilterTests.cs
git commit -m "perf(ui): time 字段比较改数值语义，免去每行格式化与解析

time 字段每行格式化一次 HH:mm:ss 字符串、操作数每行 TimeSpan.TryParse 一次。
改为 Timestamp.TimeOfDay 直接与构造时解析好的 TimeSpan 比较。"
```

---

# 批次 6：杂项收敛

## Task 6.1: CRC-16/MODBUS 改查表

**Files:**
- Modify: `src/ACCcom.Core/Services/CrcHelper.cs:11-21`
- Test: `tests/ACCcom.Core.Tests/CrcHelperTests.cs`（新建）

- [ ] **Step 1: 写测试（拿逐位实现当 oracle，钉住行为再换实现）**

```csharp
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class CrcHelperTests
{
    [Theory]
    [InlineData("")]
    [InlineData("01")]
    [InlineData("01 03")]
    [InlineData("01 03 00 00 00 01")]
    [InlineData("01 04 00 00 00 01")]
    [InlineData("FF")]
    [InlineData("FF FF FF FF FF FF FF FF")]
    public void Crc16_table_result_must_equal_the_bitwise_reference(string hex)
    {
        var data = HexHelper.TryHexStringToBytes(hex, out var bytes) ? bytes : [];

        Assert.Equal(ReferenceCrc16(data), CrcHelper.Crc16(data));
    }

    [Fact]
    public void Crc16_empty_input_is_the_init_value()
    {
        Assert.Equal(0xFFFF, CrcHelper.Crc16([]));
    }

    /// <summary>The pre-table bitwise implementation, kept verbatim as the
    /// oracle so the rewrite cannot silently change results. Deliberately not
    /// calling into CrcHelper.</summary>
    private static ushort ReferenceCrc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }
}
```

> 刻意不写死期望 CRC 常数：手算的魔数一旦算错，测试会把**错误实现**钉成正确。用逐位实现当 oracle 既避免魔数，也直接断言「换实现不改行为」。

- [ ] **Step 2: 跑测试确认通过**（此时 `CrcHelper` 还是逐位实现，测试平凡通过——这一步的价值是把 oracle 固定下来）

- [ ] **Step 3: 换查表实现**

```csharp
/// <summary>CRC-16/MODBUS lookup table for the reversed polynomial 0xA001.
/// Built once at type init; the bitwise loop cost 8 shifts per byte, this is
/// one table read.</summary>
private static readonly ushort[] Table = BuildTable();

private static ushort[] BuildTable()
{
    var table = new ushort[256];
    for (int i = 0; i < 256; i++)
    {
        ushort crc = (ushort)i;
        for (int b = 0; b < 8; b++)
            crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        table[i] = crc;
    }
    return table;
}

public static ushort Crc16(ReadOnlySpan<byte> data)
{
    ushort crc = 0xFFFF;
    foreach (var b in data)
        crc = (ushort)((crc >> 8) ^ Table[(crc ^ b) & 0xFF]);
    return crc;
}
```

- [ ] **Step 4: 跑测试确认通过（行为逐位一致）**

Run: `dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj --filter "FullyQualifiedName~CrcHelper"`

Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add src/ACCcom.Core/Services/CrcHelper.cs tests/ACCcom.Core.Tests/CrcHelperTests.cs
git commit -m "perf(core): CRC-16/MODBUS 改 256 项查表

原实现每字节 8 次移位，RTU 主站每次请求/响应、从站每次收发都调用。改为
静态初始化一次的 256 项 ushort 查表，逐位实现与查表结果按测试钉死一致。"
```

## Task 6.2: pcap 导出复用 HexHelper

**Files:**
- Modify: `src/ACCcom.Core/Services/PcapExportService.cs:81-96`
- Test: `tests/ACCcom.Core.Tests/PcapExportServiceTests.cs`（追加）

- [ ] **Step 1: 写 RED 测试**

```csharp
[Theory]
[InlineData("AA BB", new byte[] { 0xAA, 0xBB })]
[InlineData("aa-bb", new byte[] { 0xAA, 0xBB })]
public void HexToBytes_uses_the_shared_helper_contract(string hex, byte[] expected)
{
    Assert.Equal(expected, PcapExportService.HexToBytes(hex));
}

[Fact]
public void HexToBytes_rejects_odd_length_instead_of_silently_padding()
{
    // 现实现给奇数位补 0，与 HexHelper 的契约不一致，会导出错误的包
    Assert.True(HexHelper.TryHexStringToBytes("AAB", out _));
    Assert.Throws<FormatException>(() => PcapExportService.HexToBytes("AAB"));
}
```

- [ ] **Step 2: 跑测试确认失败**

Expected: FAIL —— 当前对 "AAB" 返回 `[0xAA, 0x0B]` 而不是抛异常。

- [ ] **Step 3: 换实现**

```csharp
internal static byte[] HexToBytes(string hex)
{
    if (string.IsNullOrWhiteSpace(hex)) return [];
    if (!HexHelper.TryHexStringToBytes(hex, out var bytes))
        throw new FormatException($"Invalid hex payload for pcap export: '{hex}'");
    return bytes;
}
```

> 这会让此前被静默补零接受的半截 hex 变成显式失败——**这是有意的**：导出错误的 pcap 比报错更难查。

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj --filter "FullyQualifiedName~PcapExport"`

Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git add src/ACCcom.Core/Services/PcapExportService.cs tests/ACCcom.Core.Tests/PcapExportServiceTests.cs
git commit -m "fix(core): pcap 导出复用 HexHelper，去掉第 4 套 hex 解析

PcapExportService.HexToBytes 是仓库里第 4 套 hex 解析，且每字节两次字符串
分配；它对奇数位输入静默补 0，与 HexHelper 的契约不一致，会导出错误的包。
改为调用 HexHelper.TryHexStringToBytes 并对非法输入显式抛错。"
```

## Task 6.3: 删除死代码

**Files:**
- Modify: `src/ACCcom.Core/Services/ParserManager.cs:16`、`:185`、`:197`
- Test: 无（纯删除，由编译器保证）

- [ ] **Step 1: 确认只写不读**

Run: `grep -n "_activeParserPath" src/ACCcom.Core/Services/ParserManager.cs`

Expected: 三处命中——一处声明、`:185` 置 null、`:197` 赋值，无任何读取点。

- [ ] **Step 2: 删除**

删掉三处（字段声明与两处赋值）。

- [ ] **Step 3: 构建确认**

Run: `dotnet build ACCcom.sln -c Release`

Expected: 0 警告 0 错误。

- [ ] **Step 4: 提交**

```bash
git add src/ACCcom.Core/Services/ParserManager.cs
git commit -m "chore(core): 删除只写不读的 _activeParserPath"
```

---

# 批次 7：工程配置

## Task 7.1: 启用 ServerGC / TieredPGO / R2R

**Files:**
- Modify: `src/ACCcom.McpServer/ACCcom.McpServer.csproj:10`
- Modify: `src/ACCcom/ACCcom.csproj:4-8`

- [ ] **Step 1: MCP Server 加 ServerGC**

```xml
<!-- Long-lived stdio service with no UI: ServerGC trades throughput for a
     much smaller resident set. -->
<ServerGarbageCollection>true</ServerGarbageCollection>
```

- [ ] **Step 2: WPF 加 R2R 与 TieredPGO**

```xml
<!-- R2R precompiles Roslyn/script paths — the single biggest cold-start win,
     since the first parser run compiles C# scripts. -->
<PublishReadyToRun>true</PublishReadyToRun>
<TieredPGO>true</TieredPGO>
```

- [ ] **Step 3: 构建并测量**

Run: `dotnet publish src/ACCcom/ACCcom.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/ACCcom` && `powershell -c "(Get-Item dist/ACCcom/ACCcom.exe).Length"`

记录改动前后的大小与首次启动时间，填进提交信息。

- [ ] **Step 4: 提交**

```bash
git add src/ACCcom.McpServer/ACCcom.McpServer.csproj src/ACCcom/ACCcom.csproj
git commit -m "perf(build): ServerGC / TieredPGO / ReadyToRun

MCP Server 是长驻 stdio 进程、无 UI，ServerGC 用吞吐换更小的常驻集；WPF 侧
R2R 对 Roslyn 冷启动收益最大（首次解析要编译 C# 脚本）。"
```

## Task 7.2: 启用有意义的 CA 规则（分批，勿一次开满）

**Files:**
- Modify: `Directory.Build.props:4-10`
- Modify: `.editorconfig`（新增 `[*.cs]` 诊断段）

> ⚠️ `TreatWarningsAsErrors=true` 且 `AnalysisMode=Recommended` 会一次性报出上百条。**必须分批**，每批单独提交、单独修。

- [ ] **Step 1: 先只加一条零成本的规则试水**

`.editorconfig` 末尾追加：

```ini
[*.cs]
# 真正的死代码/未使用成员 —— 零误报，值得开着
dotnet_diagnostic.CA1812.severity = warning
dotnet_diagnostic.CA1823.severity = warning
dotnet_diagnostic.CA2201.severity = warning
```

- [ ] **Step 2: 构建，看新增多少条**

Run: `dotnet build ACCcom.sln -c Release`

Expected: 若超过 20 条，先修掉再继续；不要用 `NoWarn` 一把压掉。

- [ ] **Step 3: 逐批加严**

每批加 3~5 条，跑构建，修新暴露的问题，单独提交。目标是把 `AnalysisMode` 推到 `Recommended` 而不需要大规模豁免。

- [ ] **Step 4: 补上失效的 EnforceCodeStyleInBuild**

`.editorconfig` 里补上项目实际遵守的风格规则（当前一条都没有，`EnforceCodeStyleInBuild=true` 等于空转）：

```ini
dotnet_style_qualification_for_field = false:warning
dotnet_style_qualification_for_property = false:warning
dotnet_style_qualification_for_method = false:warning
csharp_prefer_braces = true:warning
```

Run: `dotnet format ACCcom.sln style --verify-no-changes` —— 与 CI 的检查保持一致。

- [ ] **Step 5: 提交**

```bash
git add Directory.Build.props .editorconfig
git commit -m "build: 逐批启用 CA 诊断规则与代码风格规则

.editorconfig 此前零条 dotnet_diagnostic/dotnet_style 规则，EnforceCodeStyleInBuild
实际空转。分批加严，每批单独修干净再进下一批。"
```

## Task 7.3: 包版本对齐 TFM

**Files:**
- Modify: `Directory.Packages.props:12-17`

- [ ] **Step 1: 降到 8.x**

```xml
<PackageVersion Include="Microsoft.Extensions.Caching.Memory" Version="8.0.1" />
<PackageVersion Include="System.IO.Ports" Version="8.0.0" />
<PackageVersion Include="Microsoft.Extensions.Hosting" Version="8.0.1" />
```

（`global.json` 把 SDK 钉在 8.0.400，TFM 是 `net8.0`，10.0.12 的包会额外带 `unix` + `win` 两套 RID 资产。）

- [ ] **Step 2: 还原并构建**

Run: `dotnet restore && dotnet build ACCcom.sln -c Release`

Expected: 0 警告 0 错误。

- [ ] **Step 3: 提交**

```bash
git add Directory.Packages.props
git commit -m "build: Extensions/System.IO.Ports 对齐 net8.0

三个包此前锁在 10.0.12，而 global.json 把 SDK 钉在 8.0.400、TFM 是 net8.0。
功能上能跑，但会额外带 unix + win 两套 RID 资产。降到 8.x 与 TFM 对齐。"
```

## Task 7.4: MCP 客户端改指 dist/（跨仓库，需 R5 备份）

> ⚠️ 改的是 `D:\codeSMwork` 真源，**不在本仓库**。按 R5：先备份、及时 git 提交。

**症状**（本轮实测撞了 3 次）：AI 客户端把 acccom MCP 直连到
`src\ACCcom.McpServer\bin\Release\net8.0\ACCcom.McpServer.exe`。harness 一秒内把它拉起，
`dotnet build` 必然 `MSB3027 超出重试计数 10`。项目里 `.claude/settings.json` 又指向
`bin\Debug\net8.0\`。CI 发布的是 `dist\ACCcom.McpServer\`。

**Files:**
- Modify: `D:\codeSMwork\mcp\canonical.json`（若实配在此）
- Modify: 两个 live profile 的 `mcp-acccom` 条目（web L31 / desktop L742）
- Modify: `<Xcom>/.claude/settings.json:4`

- [ ] **Step 1: 备份真源**

```bash
cd /d/codeSMwork && cp mcp/canonical.json "backup/mcp-canonical.json.$(date +%Y%m%d_%H%M%S)"
git status --short
```

- [ ] **Step 2: 改所有 MCP 配置指向发布版**

把 `mcp-acccom` 的 command 改为（先 `dotnet publish` 到 `dist/`）：

```
D:\WORK_VSCODE\Vibe-coding\Xcom\dist\ACCcom.McpServer\ACCcom.McpServer.exe
```

`canonical.json` 里那条勘误注释同步更新——它记录的正是「两侧路径不是同一份构建产物」这个事实。

- [ ] **Step 3: 提交真源**

```bash
cd /d/codeSMwork && git add -A && git commit -m "fix(mcp): acccom MCP 指向 dist 发布版，不再锁住构建输出"
```

- [ ] **Step 4: 验证**

关掉所有 AI 客户端，跑 `dotnet build ACCcom.sln -c Release`，确认不再出现 `MSB3021/MSB3027`。

---

## 全局验证（每批合并前跑）

```bash
# 1. 构建（先确认没有 MCP 进程占用 bin\）
tasklist //FI "IMAGENAME eq ACCcom.McpServer.exe"
dotnet build ACCcom.sln -c Release --no-incremental

# 2. 吞吐基准（不在覆盖率插桩下跑）
dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj -c Release --filter "FullyQualifiedName~RxHotPathBenchmarkTests"
dotnet test tests/ACCcom.McpServer.Tests/ACCcom.McpServer.Tests.csproj -c Release --filter "FullyQualifiedName~RxHotPathBenchmarkTests"

# 3. 全量测试
dotnet test ACCcom.sln -c Release --filter "FullyQualifiedName!~RxHotPathBenchmarkTests"

# 4. 与 CI 一致的格式检查
dotnet format ACCcom.sln whitespace --verify-no-changes
dotnet format ACCcom.sln style --verify-no-changes
dotnet format ACCcom.sln analyzers --verify-no-changes
```

**回归底线**：`RxHotPathBenchmarkTests` 的下限断言不得退化。这套基准是本仓库已有的热路径守卫，批次 1 改动事件语义后**必须**跑——若某个断言掉到下限以下，说明 drain 化引入了额外开销，需要重新评估队列容量或批量化策略。

## 已知无关失败

`DataBufferServiceTests.WaitForQuietAsync_activity_extends_quiet_window` 在全量负载下偶发失败、隔离跑 4/4 通过。这是**预先存在**的 flaky，与本计划任何任务无关，不要为它浪费时间。