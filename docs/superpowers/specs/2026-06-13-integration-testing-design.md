# Integration Testing + Virtual Serial Port

## Motivation

ACCCOM has 195 unit tests but zero integration tests. The serial port (`System.IO.Ports.SerialPort`) is hardware-dependent, making end-to-end verification impossible in CI or normal development. We need:

1. **`ISerialService` interface** — decouple consumers from concrete serial implementation
2. **`VirtualSerialService`** — in-memory loopback for testing
3. **Integration tests** — cover Open/Close/Send/Receive, FrameAssembler, ParserEngine pipeline

## Architecture

### ISerialService Interface

Extracted from `SerialService` public members:

```csharp
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

### VirtualSerialService

In-memory implementation of `ISerialService`:

- **State machine**: tracks opened/closed state, validates config
- **TX buffer**: internal `List<LogEntry>` collecting all `Send()` calls
- **RX injection**: public `InjectRxData(string hex)` method creates a `LogEntry`, fires `OnDataReceived`, and stores in an RX buffer
- **Error simulation**: optional `SimulateError` flag to test error handling paths
- **No timers, no threads** — operations are synchronous

### Consumer Changes

| Consumer | Current type | New type |
|----------|-------------|----------|
| `HttpService._serialService` | `SerialService?` | `ISerialService?` |
| `ToolViewModel._serial` | `SerialService` | `ISerialService` |
| `DataFlowViewModel._serial` | `SerialService` | `ISerialService` |
| `MainViewModel._serial` | `SerialService` | `ISerialService` |

No behavioral changes — `SerialService` already implements all interface members.

### Integration Test Scenarios

1. **Open/Close lifecycle**: VirtualSerial → Open → IsOpen == true → Close → IsOpen == false
2. **TX path**: Open → Send → GetSentData() → verify LogEntry direction == "TX", RawHex matches
3. **RX path**: Open → InjectRxData → HttpService.Buffer receives entry → verify direction == "RX"
4. **Parser pipeline**: Open → ActivateParser → InjectRxData → entry.Fields populated
5. **FrameAssembler**: Open → Enable FrameAssembler → InjectRxData fragments → assembled frame emitted
6. **Buffer capacity**: Inject beyond capacity → oldest entries dropped
7. **Concurrent injection**: Parallel InjectRxData calls → no data loss
8. **Error handling**: Close → Send → returns false; SimulateError → OnError fires

## Files Changed

| Action | File |
|--------|------|
| Create | `src/ACCcom.Core/Services/ISerialService.cs` |
| Create | `src/ACCcom.Core/Services/VirtualSerialService.cs` |
| Modify | `src/ACCcom.Core/Services/SerialService.cs` — add `: ISerialService` |
| Modify | `src/ACCcom.Core/Services/HttpService.cs` — `SerialService?` → `ISerialService?` |
| Modify | `src/ACCcom/ViewModels/ToolViewModel.cs` — `SerialService` → `ISerialService` |
| Modify | `src/ACCcom/ViewModels/DataFlowViewModel.cs` — `SerialService` → `ISerialService` |
| Modify | `src/ACCcom/ViewModels/MainViewModel.cs` — field + params to `ISerialService` |
| Create | `tests/ACCcom.Core.Tests/SerialServiceIntegrationTests.cs` |
| Create | `tests/ACCcom.Core.Tests/VirtualSerialServiceTests.cs` (unit tests for virtual itself) |

## Test Approach

- Use xUnit `[Fact]` for each scenario
- No mocking framework — VirtualSerialService IS the test double
- Tests in `ACCcom.Core.Tests` project (no UI dependency)
- Each test creates `VirtualSerialService` + required services, exercises the pipeline, asserts on `Buffer` / `GetSentData()` / event callbacks
