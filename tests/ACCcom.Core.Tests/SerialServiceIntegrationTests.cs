using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class SerialServiceIntegrationTests : IDisposable
{
    private readonly string _tempParserDir;
    private readonly string _tempParserPath;

    public SerialServiceIntegrationTests()
    {
        _tempParserDir = Path.Combine(Path.GetTempPath(), "acccom_test_parsers_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempParserDir);
        _tempParserPath = Path.Combine(_tempParserDir, "test.csx");
        File.WriteAllText(_tempParserPath, SimpleParserCode);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempParserDir, true); }
        catch { }
    }

    // --- TX path ---

    [Fact]
    public void SendData_Appears_In_Buffer()
    {
        using var serial = new VirtualSerialService();
        using var http = new HttpService(serial);
        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
        serial.Send("Hello");
        Assert.Equal(1, http.Buffer.Count());
        var entries = http.GetEntriesSince(0);
        Assert.Single(entries);
        Assert.Equal("TX", entries[0].Direction);
    }

    [Fact]
    public void SendHexData_Appears_In_Buffer()
    {
        using var serial = new VirtualSerialService();
        using var http = new HttpService(serial);
        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
        serial.SendHex("AA 55 03");
        Assert.Equal(1, http.Buffer.Count());
        var entries = http.GetEntriesSince(0);
        Assert.Contains("AA55", entries[0].RawHex, StringComparison.OrdinalIgnoreCase);
    }

    // --- RX path ---

    [Fact]
    public void InjectedRxData_Appears_In_Buffer()
    {
        using var serial = new VirtualSerialService();
        using var http = new HttpService(serial);
        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
        serial.InjectRxData("AA 55 03 01 19");
        Assert.Equal(1, http.Buffer.Count());
        var entries = http.GetEntriesSince(0);
        Assert.Single(entries);
        Assert.Equal("RX", entries[0].Direction);
    }

    [Fact]
    public void InjectRxData_Fires_OnDataEntry_Event()
    {
        LogEntry? captured = null;
        using var serial = new VirtualSerialService();
        using var http = new HttpService(serial);
        http.OnDataEntry += e => captured = e;
        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
        serial.InjectRxData("AA 55 03");
        Assert.NotNull(captured);
        Assert.Equal("RX", captured!.Direction);
    }

    // --- Parser pipeline ---

    [Fact]
    public void RxData_With_Parser_Produces_Fields()
    {
        using var serial = new VirtualSerialService();
        using var parserManager = new ParserManager(_tempParserDir);
        using var http = new HttpService(serial, parserManager);

        Assert.True(File.Exists(_tempParserPath), $"Parser file missing: {_tempParserPath}");
        Assert.True(parserManager.Activate("test"), "Failed to activate parser 'test'");

        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
        serial.InjectRxData("AA 55 06 01 19 2E");

        var entries = http.GetEntriesSince(0);
        Assert.NotEmpty(entries);
        var rx = entries.First(e => e.Direction == "RX");
        Assert.NotNull(rx);
        Assert.NotNull(rx.Fields);
        Assert.NotEmpty(rx.Fields!);
    }

    // --- Buffer capacity ---

    [Fact]
    public void Buffer_Exceeds_Capacity_Drops_Oldest()
    {
        using var serial = new VirtualSerialService();
        using var http = new HttpService(serial, bufferCapacity: 5);
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
        using var http = new HttpService(serial);
        var result = serial.Send("test");
        Assert.False(result);
    }

    // --- Integration with HttpService API ---

    [Fact]
    public void HttpService_GetStatus_Works_With_VirtualSerial()
    {
        using var serial = new VirtualSerialService();
        using var http = new HttpService(serial);
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

        var assemblerConfig = new FrameAssemblerConfig
        {
            Enabled = true,
            Header = "AA 55",
            LengthFieldOffset = 2,
            LengthFieldSize = 1,
            MaxFrameSize = 256,
            PartialFrameTimeoutMs = 5000
        };
        var assembler = new FrameAssembler(assemblerConfig);
        LogEntry? assembled = null;
        assembler.OnFrameAssembled += e => assembled = e;

        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        LogEntry? captured = null;
        serial.OnDataReceived += e =>
        {
            captured = e;
            if (e.Direction == "RX")
                assembler.Feed(e);
        };

        serial.InjectRxData("AA 55 06");
        Assert.Null(assembled);

        serial.InjectRxData("01 19 2E");
        Assert.NotNull(assembled);
        Assert.Equal("AA 55 06 01 19 2E", assembled!.RawHex);
    }

    // --- Concurrent injection ---

    [Fact]
    public void ConcurrentInjectRxData_NoDataLoss()
    {
        using var serial = new VirtualSerialService();
        using var http = new HttpService(serial);
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
result.Add(new FieldAnnotation { Name = ""Header"", Offset = 0, Length = 2, RawHex = RawHex(0, 2), DisplayValue = RawHex(0, 2), Color = ""#22C55E"", Severity = FieldSeverity.Normal });
result.Add(new FieldAnnotation { Name = ""Length"", Offset = 2, Length = 1, RawHex = RawHex(2, 1), DisplayValue = ToUInt16(2, false).ToString(), Color = ""#3B82F6"", Severity = FieldSeverity.Normal });
result.Add(new FieldAnnotation { Name = ""Cmd"", Offset = 3, Length = 1, RawHex = RawHex(3, 1), DisplayValue = ToUInt16(3, false).ToString(), Color = ""#F59E0B"", Severity = FieldSeverity.Normal });
return result;
";
}
