using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class ModbusServiceTests
{
    [Fact]
    public void Crc16_CalculatesCorrectly()
    {
        var data = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x01 };
        var crc = ModbusService.Crc16(data.AsSpan());
        Assert.Equal(0x0A84, crc);
    }

    [Fact]
    public void Crc16_EmptyData_Returns0xFFFF()
    {
        var crc = ModbusService.Crc16(ReadOnlySpan<byte>.Empty);
        Assert.Equal(0xFFFF, crc);
    }

    [Fact]
    public async Task ReadHoldingRegisters_WithVirtualSerial_Success()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        // Start the request first (it sends the frame synchronously before
        // awaiting), then inject the device response — deterministic ordering,
        // no Task.Delay scheduling gamble.
        var request = modbus.ReadHoldingRegistersAsync(0x01, 0x00, 0x02);
        virtualSerial.InjectRxData("01 03 04 00 0A 00 64 DB DA");
        var result = await request;

        Assert.False(result.IsError);
        Assert.Equal(0x01, result.SlaveId);
        Assert.Equal(ModbusFunctionCode.ReadHoldingRegisters, result.FunctionCode);
        Assert.Equal(5, result.Data.Length);
        Assert.Equal(new byte[] { 0x04, 0x00, 0x0A, 0x00, 0x64 }, result.Data);
    }

    [Fact]
    public async Task ReadHoldingRegisters_Timeout_ReturnsError()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        var result = await modbus.ReadHoldingRegistersAsync(0x01, 0x00, 0x01, timeoutMs: 100);

        Assert.True(result.IsError);
        Assert.Contains("Timeout", result.ErrorMessage);
    }

    [Fact]
    public async Task WriteSingleRegister_WithVirtualSerial_Success()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        var request = modbus.WriteSingleRegisterAsync(0x01, 0x0001, 0x0102);
        virtualSerial.InjectRxData("01 06 00 01 01 02 58 5B");
        var result = await request;

        Assert.False(result.IsError);
        Assert.Equal(0x01, result.SlaveId);
    }

    private sealed class ThrowingTransport : IModbusTransport
    {
        public Exception ToThrow { get; set; } = new("boom");
        public Task<byte[]> SendReceiveAsync(byte slaveId, byte functionCode, byte[] pdu, int timeoutMs, CancellationToken ct = default)
            => Task.FromException<byte[]>(ToThrow);
        public void Dispose() { }
    }

    [Fact]
    public async Task GenericException_StillLogsTransaction()
    {
        // Timeout/cancel/IO-invalid failures always landed in the
        // transaction log; the generic catch skipped it, so a crashed
        // request silently vanished from the GUI log. Every failure path
        // must notify.
        using var modbus = new ModbusService(new ThrowingTransport { ToThrow = new InvalidOperationException("Test failure") });
        ModbusTransaction? logged = null;
        modbus.OnTransaction += tx => logged = tx;

        var result = await modbus.ReadHoldingRegistersAsync(0x01, 0x00, 0x01, timeoutMs: 500);

        Assert.True(result.IsError);
        Assert.NotNull(logged);
        Assert.False(logged!.IsSuccess);
        Assert.Contains("Test failure", logged.ErrorMessage);
    }

    [Fact]
    public async Task ExceptionResponse_ParsesCorrectly()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        var request = modbus.ReadHoldingRegistersAsync(0x01, 0xFFFF, 0x01, timeoutMs: 2000);
        virtualSerial.InjectRxData("01 83 02 C0 F1");
        var result = await request;

        Assert.True(result.IsError);
        Assert.Equal((byte)0x02, result.ExceptionCode!.Value);
    }

    [Fact]
    public async Task CrcMismatch_WaitsThenTimesOut()
    {
        // With CRC-aligned reassembly a corrupt frame is indistinguishable
        // from a truncated one still arriving — the request waits out its
        // timeout (previously it failed fast with a per-chunk CRC error that
        // misfired on split responses). The error surfaces as a timeout.
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        var request = modbus.ReadHoldingRegistersAsync(0x01, 0x00, 0x01, timeoutMs: 300);
        virtualSerial.InjectRxData("01 03 02 00 0A 00 00");
        var result = await request;

        Assert.True(result.IsError);
        Assert.Contains("Timeout", result.ErrorMessage);
    }

    [Fact]
    public async Task MultipleConcurrentReads_ResolveCorrectly()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        // The bus gate serializes the two requests — one transaction on the
        // wire at a time — so each response is injected at the moment its OWN
        // request frame goes out (TX hook matched by slave id). Routing by
        // slave id + function is what pairs each response with its caller.
        var t1 = SendAndRespond(modbus, virtualSerial, 0x01, "01 03 02 00 0A 38 43");
        var t2 = SendAndRespond(modbus, virtualSerial, 0x02, "02 03 02 00 14 FC 4B");

        var results = await Task.WhenAll(t1, t2);
        Assert.False(results[0].IsError);
        Assert.False(results[1].IsError);
    }

    /// <summary>Starts a read and injects the hex response at the moment its
    /// own TX frame hits the wire (matched by leading slave id) — deterministic
    /// under the bus gate, which serializes concurrent requests.</summary>
    private static Task<ModbusResponse> SendAndRespond(ModbusService modbus, VirtualSerialService serial, byte slaveId, string responseHex)
    {
        void OnTx(LogEntry e)
        {
            if (e.Direction != "TX") return;
            var compact = (e.RawHex ?? "").Replace(" ", "");
            if (compact.Length < 2) return;
            if (Convert.ToByte(compact.Substring(0, 2), 16) != slaveId) return;

            serial.OnDataReceived -= OnTx;
            serial.InjectRxData(responseHex);
        }

        serial.OnDataReceived += OnTx;
        return modbus.ReadHoldingRegistersAsync(slaveId, 0x00, 0x01);
    }

    [Fact]
    public async Task WriteSingleCoil_On_Success()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        var request = modbus.WriteSingleCoilAsync(0x01, 0x0001, true);
        virtualSerial.InjectRxData("01 05 00 01 FF 00 DD FA");
        var result = await request;

        Assert.False(result.IsError);
        Assert.Equal(0x01, result.SlaveId);
    }

    [Fact]
    public async Task WriteMultipleCoils_Success()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        var respAdu = new byte[] { 0x01, 0x0F, 0x00, 0x00, 0x00, 0x08 };
        var crc = ModbusService.Crc16(respAdu.AsSpan());
        var respHex = $"01 0F 00 00 00 08 {crc & 0xFF:X2} {crc >> 8:X2}";

        var request = modbus.WriteMultipleCoilsAsync(0x01, 0x0000, [true, false, true, false, true, false, true, false]);
        virtualSerial.InjectRxData(respHex);
        var result = await request;

        Assert.False(result.IsError);
        Assert.Equal(0x01, result.SlaveId);
        Assert.Equal(ModbusFunctionCode.WriteMultipleCoils, result.FunctionCode);
    }

    [Fact]
    public async Task WriteMultipleRegisters_Success()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        var respAdu = new byte[] { 0x01, 0x10, 0x00, 0x01, 0x00, 0x03 };
        var crc = ModbusService.Crc16(respAdu.AsSpan());
        var respHex = $"01 10 00 01 00 03 {crc & 0xFF:X2} {crc >> 8:X2}";

        var request = modbus.WriteMultipleRegistersAsync(0x01, 0x0001, [0x0A, 0x0B, 0x0C]);
        virtualSerial.InjectRxData(respHex);
        var result = await request;

        Assert.False(result.IsError);
        Assert.Equal(0x01, result.SlaveId);
        Assert.Equal(ModbusFunctionCode.WriteMultipleRegisters, result.FunctionCode);
    }

    [Fact]
    public async Task WriteMultipleCoils_Exception_ReturnsError()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        var request = modbus.WriteMultipleCoilsAsync(0x01, 0x0000, [true], timeoutMs: 2000);
        // Correct CRC over "01 8F 03" = 0x3104 → wire low-first "04 31". The
        // frame used to be injected with a BOGUS CRC ("71 C9") and still
        // completed — exception responses (func | 0x80) skipped CRC
        // validation entirely. They are validated like any other frame now,
        // so only a correct CRC parses.
        virtualSerial.InjectRxData("01 8F 03 04 31");
        var result = await request;

        Assert.True(result.IsError);
        Assert.Equal((byte)0x03, result.ExceptionCode!.Value);
    }

    [Fact]
    public async Task WriteMultipleRegisters_Timeout_ReturnsError()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        var result = await modbus.WriteMultipleRegistersAsync(0x01, 0x0000, [0x0001], timeoutMs: 100);

        Assert.True(result.IsError);
        Assert.Contains("Timeout", result.ErrorMessage);
    }

    [Fact]
    public async Task WriteMultipleCoils_null_or_empty_values_returns_error()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        var nullResult = await modbus.WriteMultipleCoilsAsync(0x01, 0x0000, null);
        Assert.True(nullResult.IsError);
        Assert.Contains("values", nullResult.ErrorMessage);

        var emptyResult = await modbus.WriteMultipleCoilsAsync(0x01, 0x0000, []);
        Assert.True(emptyResult.IsError);
        Assert.Contains("values", emptyResult.ErrorMessage);

        // No request frame was sent for the invalid input.
        Assert.Empty(virtualSerial.GetSentData());
    }

    [Fact]
    public async Task WriteMultipleRegisters_null_or_empty_values_returns_error()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        var nullResult = await modbus.WriteMultipleRegistersAsync(0x01, 0x0000, null);
        Assert.True(nullResult.IsError);
        Assert.Contains("values", nullResult.ErrorMessage);

        var emptyResult = await modbus.WriteMultipleRegistersAsync(0x01, 0x0000, []);
        Assert.True(emptyResult.IsError);
        Assert.Contains("values", emptyResult.ErrorMessage);

        Assert.Empty(virtualSerial.GetSentData());
    }

    [Fact]
    public async Task ReadWriteMultipleRegisters_null_write_values_returns_error()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        var result = await modbus.ReadWriteMultipleRegistersAsync(0x01, 0x0000, 1, 0x0000, null);

        Assert.True(result.IsError);
        Assert.Contains("writeValues", result.ErrorMessage);
        Assert.Empty(virtualSerial.GetSentData());
    }

    [Fact]
    public async Task MaskWriteRegister_SendsCorrectPdu()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        // addr=0x0004, andMask=0x00F2, orMask=0x0025 — the full ADU
        // (slaveId + func 0x16 + 6-byte PDU + CRC) must carry the mask bytes
        // intact; a shrinking-buffer bug previously let the CRC overwrite the
        // PDU's last byte (0x25 became 0x4D).
        var request = modbus.MaskWriteRegisterAsync(0x01, 0x0004, 0x00F2, 0x0025, timeoutMs: 2000);
        virtualSerial.InjectRxData("01 16 00 04 00 F2 00 25 67 EE"); // echo response
        var result = await request;

        Assert.False(result.IsError);
        Assert.Equal(0x01, result.SlaveId);
        var sent = virtualSerial.GetSentData();
        var pdu = Assert.Single(sent);
        // Tolerant of the canonical spaced TX layout; the regression being
        // guarded is byte content (0x25 not 0x4D), not spacing.
        Assert.Equal("0116000400F2002567EE", pdu.RawHex.Replace(" ", ""));
    }

    [Fact]
    public async Task MaskWriteRegister_ExceptionResponse_ReportsExceptionCode()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        var request = modbus.MaskWriteRegisterAsync(0x01, 0x0004, 0x00F2, 0x0025, timeoutMs: 2000);
        virtualSerial.InjectRxData("01 96 02 CE 61"); // 0x16 | 0x80, exception 0x02
        var result = await request;

        Assert.True(result.IsError);
        Assert.Equal((byte)0x02, result.ExceptionCode!.Value);
    }
}
