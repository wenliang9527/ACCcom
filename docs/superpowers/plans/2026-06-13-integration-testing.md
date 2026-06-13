# Integration Testing + Virtual Serial Port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract `ISerialService` interface, build `VirtualSerialService` for in-memory loopback, and write integration tests covering the full serial → parser → buffer pipeline.

**Architecture:** Extract interface from existing `SerialService`, create in-memory `VirtualSerialService` that implements it, update all consumers to depend on the interface, then test end-to-end without hardware.

**Tech Stack:** C# / .NET 8 / xUnit / System.IO.Ports (for real impl only)

---

### Task 1: Create ISerialService interface

**Files:**
- Create: `src/ACCcom.Core/Services/ISerialService.cs`

- [ ] **Step 1: Create ISerialService.cs**

```csharp
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public interface ISerialService
{
    bool IsOpen { get; }
    string? CurrentPort { get; }
    int BaudRate { get; }
    event Action<LogEntry>? OnDataReceived;
    event Action<string>? OnError;
    event Action? OnDisconnected;
    bool Open(SerialConfig config);
    bool Send(string data, bool isHex = false);
    bool SendHex(string hex);
    bool Close();
    void EnableAutoReconnect(bool enable, int maxAttempts = 10, int delayMs = 1000);
    void UpdateReconnectSettings(ReconnectSettings settings);
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ACCcom.Core/ACCcom.Core.csproj -nologo`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/ACCcom.Core/Services/ISerialService.cs
git commit -m "feat: add ISerialService interface"
```

---

### Task 2: SerialService implements ISerialService

**Files:**
- Modify: `src/ACCcom.Core/Services/SerialService.cs:7`

- [ ] **Step 1: Add interface to class declaration**

```csharp
public class SerialService : ISerialService, IDisposable
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ACCcom.Core/ACCcom.Core.csproj -nologo`
Expected: 0 errors

- [ ] **Step 3: Run existing tests**

Run: `dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj -nologo --no-restore`
Expected: 195 passed (no regressions)

- [ ] **Step 4: Commit**

```bash
git add src/ACCcom.Core/Services/SerialService.cs
git commit -m "feat: SerialService implements ISerialService"
```

---

### Task 3: Create VirtualSerialService (with unit tests)

**Files:**
- Create: `src/ACCcom.Core/Services/VirtualSerialService.cs`
- Create: `tests/ACCcom.Core.Tests/VirtualSerialServiceTests.cs`

- [ ] **Step 1: Write failing test for VirtualSerialService**

```csharp
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class VirtualSerialServiceTests
{
    [Fact]
    public void Open_Sets_IsOpen_True()
    {
        var svc = new VirtualSerialService();
        var config = new SerialConfig { PortName = "VIRTUAL", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 };
        var result = svc.Open(config);
        Assert.True(result);
        Assert.True(svc.IsOpen);
        Assert.Equal("VIRTUAL", svc.CurrentPort);
        Assert.Equal(115200, svc.BaudRate);
    }

    [Fact]
    public void Close_Sets_IsOpen_False()
    {
        var svc = new VirtualSerialService();
        svc.Open(new SerialConfig { PortName = "VIRTUAL", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
        var result = svc.Close();
        Assert.True(result);
        Assert.False(svc.IsOpen);
    }

    [Fact]
    public void Send_Without_Open_Returns_False()
    {
        var svc = new VirtualSerialService();
        var result = svc.Send("test");
        Assert.False(result);
    }

    [Fact]
    public void Send_Stores_Entry_In_SentData()
    {
        var svc = new VirtualSerialService();
        svc.Open(new SerialConfig { PortName = "VIRTUAL", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
        var result = svc.Send("Hello");
        Assert.True(result);
        var sent = svc.GetSentData();
        Assert.Single(sent);
        Assert.Equal("TX", sent[0].Direction);
        Assert.Equal("Hello", sent[0].Text);
    }

    [Fact]
    public void SendHex_Stores_Entry()
    {
        var svc = new VirtualSerialService();
        svc.Open(new SerialConfig { PortName = "VIRTUAL", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
        svc.SendHex("AA55");
        var sent = svc.GetSentData();
        Assert.Single(sent);
        Assert.Contains("AA55", sent[0].RawHex, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InjectRxData_Fires_OnDataReceived()
    {
        var svc = new VirtualSerialService();
        svc.Open(new SerialConfig { PortName = "VIRTUAL", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
        LogEntry? received = null;
        svc.OnDataReceived += e => received = e;
        svc.InjectRxData("AA 55 03 01 19");
        Assert.NotNull(received);
        Assert.Equal("RX", received!.Direction);
        Assert.Contains("AA55", received.RawHex, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InjectRxData_Without_Open_Does_Not_Throw()
    {
        var svc = new VirtualSerialService();
        var ex = Record.Exception(() => svc.InjectRxData("AA 55"));
        Assert.Null(ex);
    }

    [Fact]
    public void Close_Disconnects_Without_Error()
    {
        var svc = new VirtualSerialService();
        svc.Open(new SerialConfig { PortName = "VIRTUAL", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
        svc.Close();
        Assert.False(svc.IsOpen);
    }
}
```

- [ ] **Step 2: Run tests — expect build failure (no VirtualSerialService)**

Run: `dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj -nologo`
Expected: Build error — `VirtualSerialService` not found

- [ ] **Step 3: Implement VirtualSerialService**

```csharp
using System.Collections.Generic;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class VirtualSerialService : ISerialService
{
    private bool _isOpen;
    private string? _currentPort;
    private int _baudRate;
    private int _nextRxId;

    private readonly List<LogEntry> _sentData = new();
    private readonly object _lock = new();

    public bool IsOpen => _isOpen;
    public string? CurrentPort => _currentPort;
    public int BaudRate => _baudRate;

    public event Action<LogEntry>? OnDataReceived;
    public event Action<string>? OnError;
    public event Action? OnDisconnected;

    public bool Open(SerialConfig config)
    {
        _currentPort = config.PortName;
        _baudRate = config.BaudRate;
        _isOpen = true;
        return true;
    }

    public bool Close()
    {
        _isOpen = false;
        _currentPort = null;
        _baudRate = 0;
        OnDisconnected?.Invoke();
        return true;
    }

    public bool Send(string data, bool isHex = false)
    {
        if (!_isOpen)
        {
            OnError?.Invoke("Serial port not open");
            return false;
        }

        var textBytes = System.Text.Encoding.UTF8.GetBytes(data);
        var hexStr = isHex ? data.Replace(" ", "") :
            BitConverter.ToString(textBytes).Replace("-", " ");

        var entry = new LogEntry
        {
            Id = _sentData.Count + 1,
            Timestamp = DateTime.Now,
            Direction = "TX",
            RawHex = hexStr,
            Text = data
        };

        lock (_lock) _sentData.Add(entry);
        OnDataReceived?.Invoke(entry);
        return true;
    }

    public bool SendHex(string hex) => Send(hex, true);

    public void InjectRxData(string hex)
    {
        var hexNoSpace = hex.Replace(" ", "");
        var bytes = Convert.FromHexString(hexNoSpace);

        var entry = new LogEntry
        {
            Id = Interlocked.Increment(ref _nextRxId),
            Timestamp = DateTime.Now,
            Direction = "RX",
            RawHex = BitConverter.ToString(bytes).Replace("-", " "),
            Text = System.Text.Encoding.UTF8.GetString(bytes)
        };

        OnDataReceived?.Invoke(entry);
    }

    public List<LogEntry> GetSentData()
    {
        lock (_lock) return new List<LogEntry>(_sentData);
    }

    public int SentCount
    {
        get { lock (_lock) return _sentData.Count; }
    }

    public void ClearSentData()
    {
        lock (_lock) _sentData.Clear();
    }

    public void EnableAutoReconnect(bool enable, int maxAttempts = 10, int delayMs = 1000) { }
    public void UpdateReconnectSettings(ReconnectSettings settings) { }
}
```

- [ ] **Step 4: Build and run new tests**

Run: `dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj -nologo`
Expected: All VirtualSerialServiceTests pass

- [ ] **Step 5: Commit**

```bash
git add src/ACCcom.Core/Services/VirtualSerialService.cs tests/ACCcom.Core.Tests/VirtualSerialServiceTests.cs
git commit -m "feat: add VirtualSerialService for in-memory serial simulation"
```

---

### Task 4: Update HttpService to use ISerialService

**Files:**
- Modify: `src/ACCcom.Core/Services/HttpService.cs`

- [ ] **Step 1: Change field and constructor type**

In `HttpService.cs`:
```csharp
// line 15
private readonly ISerialService? _serialService;

// line 20
public HttpService(ISerialService? serialService = null, ParserManager? parserManager = null, string url = DefaultUrl, int bufferCapacity = 10000)
```

- [ ] **Step 2: Build and run tests**

Run: `dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj -nologo`
Expected: All existing tests pass

- [ ] **Step 3: Commit**

```bash
git add src/ACCcom.Core/Services/HttpService.cs
git commit -m "feat: HttpService uses ISerialService interface"
```

---

### Task 5: Write integration tests

**Files:**
- Create: `tests/ACCcom.Core.Tests/SerialServiceIntegrationTests.cs`

- [ ] **Step 1: Write failing integration tests**

```csharp
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class SerialServiceIntegrationTests
{
    // --- TX path ---

    [Fact]
    public void SendData_Appears_In_Buffer()
    {
        using var serial = new VirtualSerialService();
        var http = new HttpService(serial);
        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
        serial.Send("Hello");
        Assert.Equal(1, http.Buffer.Count());
        var entries = http.Buffer.GetEntriesSince(0);
        Assert.Single(entries);
        Assert.Equal("TX", entries[0].Direction);
    }

    [Fact]
    public void SendHexData_Appears_In_Buffer()
    {
        using var serial = new VirtualSerialService();
        var http = new HttpService(serial);
        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
        serial.SendHex("AA 55 03");
        Assert.Equal(1, http.Buffer.Count());
        var entries = http.Buffer.GetEntriesSince(0);
        Assert.Contains("AA55", entries[0].RawHex, StringComparison.OrdinalIgnoreCase);
    }

    // --- RX path ---

    [Fact]
    public void InjectedRxData_Appears_In_Buffer()
    {
        using var serial = new VirtualSerialService();
        var http = new HttpService(serial);
        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
        serial.InjectRxData("AA 55 03 01 19");
        Assert.Equal(1, http.Buffer.Count());
        var entries = http.Buffer.GetEntriesSince(0);
        Assert.Single(entries);
        Assert.Equal("RX", entries[0].Direction);
    }

    [Fact]
    public void InjectRxData_Fires_OnDataEntry_Event()
    {
        LogEntry? captured = null;
        using var serial = new VirtualSerialService();
        var http = new HttpService(serial);
        http.OnDataEntry += e => captured = e;
        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
        serial.InjectRxData("AA 55 03");
        Assert.NotNull(captured);
        Assert.Equal("RX", captured!.Direction);
    }

    // --- Parser pipeline ---

    [Fact]
    public async Task RxData_With_Parser_Produces_Fields()
    {
        using var serial = new VirtualSerialService();
        using var parserManager = new ParserManager();
        var http = new HttpService(serial, parserManager);

        // Load and activate a simple parser
        Assert.True(parserManager.Load(SimpleParserCode));
        parserManager.Activate("test");

        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
        serial.InjectRxData("AA 55 06 01 19 2E");

        var entries = http.Buffer.GetEntriesSince(0);
        Assert.NotEmpty(entries);
        var rx = entries.First(e => e.Direction == "RX");
        Assert.NotNull(rx);
        // Parser should produce fields for the RX entry
        // (FrameAssembler is not enabled, so entry.Fields set via parser)
        Assert.NotNull(rx.Fields);
        Assert.NotEmpty(rx.Fields!);
    }

    // --- Buffer capacity ---

    [Fact]
    public void Buffer_Exceeds_Capacity_Drops_Oldest()
    {
        using var serial = new VirtualSerialService();
        var http = new HttpService(serial, bufferCapacity: 5);
        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
        for (int i = 0; i < 10; i++)
            serial.Send($"Message {i}");

        Assert.Equal(5, http.Buffer.Count());
    }

    // --- Error handling ---

    [Fact]
    public void Send_Without_Open_Returns_False()
    {
        using var serial = new VirtualSerialService();
        var http = new HttpService(serial);
        var result = serial.Send("test");
        Assert.False(result);
    }

    // --- Integration with HttpService API ---

    [Fact]
    public void HttpService_GetStatus_Works_With_VirtualSerial()
    {
        using var serial = new VirtualSerialService();
        var http = new HttpService(serial);
        serial.Open(new SerialConfig { PortName = "TEST", BaudRate = 9600, DataBits = 8, StopBits = 1, Parity = 0 });
        serial.Send("ping");
        var status = http.GetStatus();
        Assert.NotNull(status);
    }

    // --- FrameAssembler integration ---

    [Fact]
    public void FrameAssembler_Assembles_Fragments_From_VirtualSerial()
    {
        using var serial = new VirtualSerialService();
        using var parserManager = new ParserManager();
        var http = new HttpService(serial, parserManager);

        var assemblerConfig = new FrameAssemblerConfig
        {
            Enabled = true,
            Header = "AA 55",
            LengthFieldOffset = 2,
            LengthFieldSize = 1,
            MaxFrameSize = 256,
            PartialFrameTimeoutMs = 5000
        };
        var assembler = new FrameAssembler(assemblerConfig, parserManager);
        LogEntry? assembled = null;
        assembler.OnFrameAssembled += e => assembled = e;

        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        // Inject fragment 1 (header + length = full frame len 6)
        serial.InjectRxData("AA 55 06");
        Assert.Null(assembled);

        // Inject fragment 2 (rest of frame)
        serial.InjectRxData("01 19 2E");
        Assert.NotNull(assembled);
        Assert.Equal("AA 55 06 01 19 2E", assembled!.RawHex);
    }

    // --- Concurrent injection ---

    [Fact]
    public void ConcurrentInjectRxData_NoDataLoss()
    {
        using var serial = new VirtualSerialService();
        var http = new HttpService(serial);
        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        int received = 0;
        serial.OnDataReceived += _ => Interlocked.Increment(ref received);

        const int count = 100;
        Parallel.For(0, count, i =>
        {
            serial.InjectRxData($"AA 55 03 {i:X2}");
        });

        Assert.Equal(count, received);
        Assert.Equal(count, http.Buffer.Count());
    }

    private const string SimpleParserCode = @"
var result = new List<FieldAnnotation>();
result.Add(new FieldAnnotation { Name = ""Header"", Offset = 0, Length = 2, RawHex = RawHex(0, 2), DisplayValue = RawHex(0, 2), Color = ""#22C55E"", Severity = FieldSeverity.Info });
result.Add(new FieldAnnotation { Name = ""Length"", Offset = 2, Length = 1, RawHex = RawHex(2, 1), DisplayValue = ToUInt16(2, false).ToString(), Color = ""#3B82F6"", Severity = FieldSeverity.Info });
result.Add(new FieldAnnotation { Name = ""Cmd"", Offset = 3, Length = 1, RawHex = RawHex(3, 1), DisplayValue = ToUInt16(3, false).ToString(), Color = ""#F59E0B"", Severity = FieldSeverity.Info });
return result;
";
}
```

- [ ] **Step 2: Run new tests to verify they fail (or pass)**

Run: `dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj -nologo`
Expected: Build OK, tests pass

- [ ] **Step 3: Commit**

```bash
git add tests/ACCcom.Core.Tests/SerialServiceIntegrationTests.cs
git commit -m "test: add end-to-end integration tests with VirtualSerialService"
```

---

### Task 6: Update ViewModels to use ISerialService

**Files:**
- Modify: `src/ACCcom/ViewModels/ToolViewModel.cs`
- Modify: `src/ACCcom/ViewModels/DataFlowViewModel.cs`
- Modify: `src/ACCcom/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Update ToolViewModel.cs**

Change field and constructor parameter type:
```csharp
// line 11: field
private readonly ISerialService _serial;

// lines 106-107: constructor signature
public ToolViewModel(
    ISerialService serial,
```

- [ ] **Step 2: Update DataFlowViewModel.cs**

Change field and constructor parameter type:
```csharp
// line 14: field
private readonly ISerialService _serial;

// lines 144-145: constructor signature
public DataFlowViewModel(
    ISerialService serial,
```

- [ ] **Step 3: Update MainViewModel.cs**

Change field and all usages:
```csharp
// line 14: field
private readonly ISerialService _serial = new SerialService();

// All constructor parameters are passed through — no signature changes needed
// because the concrete types are compatible via implicit conversion
```

- [ ] **Step 4: Build solution**

Run: `dotnet build ACCcom.sln -nologo`
Expected: 0 errors, 0 warnings

- [ ] **Step 5: Run all tests**

Run: `dotnet test tests/ACCcom.Core.Tests/ACCcom.Core.Tests.csproj -nologo`
Expected: All tests pass

- [ ] **Step 6: Commit**

```bash
git add src/ACCcom/ViewModels/ToolViewModel.cs src/ACCcom/ViewModels/DataFlowViewModel.cs src/ACCcom/ViewModels/MainViewModel.cs
git commit -m "refactor: ViewModels use ISerialService interface"
```
