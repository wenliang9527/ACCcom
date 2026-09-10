using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

/// <summary>
/// Modbus RTU master transport over a virtual serial service: builds correct
/// ADUs, matches responses by (slave, function), verifies CRC, and surfaces
/// exceptions / timeouts.
/// </summary>
public class ModbusRtuTransportTests
{
    private static VirtualSerialService OpenVirtual()
    {
        var serial = new VirtualSerialService();
        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200 });
        return serial;
    }

    [Fact]
    public void Constructor_null_serial_throws()
    {
        // The ctor subscribes to OnDataReceived; a null service would NRE there
        // instead of at the contract boundary.
        Assert.Throws<ArgumentNullException>(() => new ModbusRtuTransport(null!));
    }

    [Fact]
    public async Task SendReceiveAsync_WithValidResponse_ReturnsPdu()
    {
        using var serial = OpenVirtual();
        using var transport = new ModbusRtuTransport(serial);

        // Start the request first (it sends the frame synchronously before
        // awaiting), then inject the response — deterministic, no delay race.
        var request = transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 2000);
        var adu = new byte[] { 0x01, 0x03, 0x02, 0x12, 0x34 };
        var crc = CrcHelper.Crc16(adu);
        var frame = adu.Concat(new byte[] { (byte)(crc & 0xFF), (byte)(crc >> 8) }).ToArray();
        serial.InjectRxData(HexHelper.BytesToHexSpaced(frame, 0, frame.Length));
        var response = await request;

        Assert.Equal([0x01, 0x03, 0x02, 0x12, 0x34], response);
    }

    [Fact]
    public async Task SendReceiveAsync_ExceptionResponse_ReturnsExceptionFrame()
    {
        using var serial = OpenVirtual();
        using var transport = new ModbusRtuTransport(serial);

        // Slave replies with an exception: [01 83 02] + CRC (func | 0x80).
        var request = transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 2000);
        var adu = new byte[] { 0x01, 0x83, 0x02 };
        var crc = CrcHelper.Crc16(adu);
        var frame = adu.Concat(new byte[] { (byte)(crc & 0xFF), (byte)(crc >> 8) }).ToArray();
        serial.InjectRxData(HexHelper.BytesToHexSpaced(frame, 0, frame.Length));
        var response = await request;

        // Exception frames surface as the raw body (caller inspects func|0x80).
        Assert.Equal([0x01, 0x83, 0x02], response);
    }

    [Fact]
    public async Task SendReceiveAsync_BadCrc_Throws()
    {
        using var serial = OpenVirtual();
        using var transport = new ModbusRtuTransport(serial);

        var request = transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 2000);
        // Wrong CRC on purpose.
        serial.InjectRxData("01 03 02 12 34 00 00");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await request);
        Assert.Contains("CRC", ex.Message);
    }

    [Fact]
    public async Task SendReceiveAsync_WrongSlaveId_NotMatched_TimesOut()
    {
        using var serial = OpenVirtual();
        using var transport = new ModbusRtuTransport(serial);

        // Response for slave 0x02, but we asked for slave 0x01 — the request
        // keeps waiting and times out. Injecting after the request is issued
        // still exercises the no-match path deterministically.
        var request = transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 300);
        var adu = new byte[] { 0x02, 0x03, 0x02, 0x12, 0x34 };
        var crc = CrcHelper.Crc16(adu);
        var frame = adu.Concat(new byte[] { (byte)(crc & 0xFF), (byte)(crc >> 8) }).ToArray();
        serial.InjectRxData(HexHelper.BytesToHexSpaced(frame, 0, frame.Length));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await request);
    }

    [Fact]
    public async Task SendReceiveAsync_NonPositiveTimeout_ThrowsOperationCanceledNotArgument()
    {
        // A non-positive timeout used to throw ArgumentOutOfRangeException
        // inside CancellationTokenSource; it must behave as an immediate
        // timeout instead (OperationCanceledException).
        using var serial = OpenVirtual();
        using var transport = new ModbusRtuTransport(serial);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 0));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], -100));
    }

    [Fact]
    public void SendReceiveAsync_WhenSerialNotOpen_Throws()
    {
        using var serial = new VirtualSerialService(); // never opened
        using var transport = new ModbusRtuTransport(serial);

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 500));
    }

    [Fact]
    public void SendReceiveAsync_NullPdu_ThrowsArgumentNull()
    {
        // A null PDU would NRE deep inside BuildAdu (pdu.Length); it must fail
        // with ArgumentNullException at the entry point instead.
        using var serial = new VirtualSerialService();
        using var transport = new ModbusRtuTransport(serial);

        Assert.ThrowsAsync<ArgumentNullException>(() =>
            transport.SendReceiveAsync(0x01, 0x03, null!, 500));
    }

    [Fact]
    public void Dispose_CancelsPendingRequests()
    {
        using var serial = OpenVirtual();
        var transport = new ModbusRtuTransport(serial);

        var task = transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 10000);
        transport.Dispose();

        Assert.ThrowsAnyAsync<ObjectDisposedException>(() => task);
    }

    // ── ADU builders ──

    [Fact]
    public void BuildReadRequest_ProducesPdu()
    {
        Assert.Equal([0x00, 0x10, 0x00, 0x0A],
            ModbusRtuTransport.BuildReadRequest(0x10, 10));
    }

    [Fact]
    public void BuildWriteCoilRequest_On_ProducesFF00()
    {
        Assert.Equal([0x00, 0x03, 0xFF, 0x00],
            ModbusRtuTransport.BuildWriteCoilRequest(0x03, true));
    }

    [Fact]
    public void BuildWriteCoilRequest_Off_Produces0000()
    {
        Assert.Equal([0x00, 0x03, 0x00, 0x00],
            ModbusRtuTransport.BuildWriteCoilRequest(0x03, false));
    }

    [Fact]
    public void BuildWriteRegisterRequest_ProducesValue()
    {
        Assert.Equal([0x00, 0x05, 0xAB, 0xCD],
            ModbusRtuTransport.BuildWriteRegisterRequest(0x05, 0xABCD));
    }

    [Fact]
    public void BuildWriteCoilsRequest_PacksBits()
    {
        var pdu = ModbusRtuTransport.BuildWriteCoilsRequest(0x02, [true, false, true, false, false, false, false, false]);
        Assert.Equal([0x00, 0x02, 0x00, 0x08, 0x01, 0b0101], pdu);
    }

    [Fact]
    public void BuildWriteRegistersRequest_PacksValues()
    {
        var pdu = ModbusRtuTransport.BuildWriteRegistersRequest(0x02, [0x000A, 0x0064]);
        Assert.Equal([0x00, 0x02, 0x00, 0x02, 0x04, 0x00, 0x0A, 0x00, 0x64], pdu);
    }

    [Fact]
    public void BuildReadWriteRegistersRequest_Packs()
    {
        var pdu = ModbusRtuTransport.BuildReadWriteRegistersRequest(0x00, 2, 0x05, [0xAABB]);
        Assert.Equal([0x00, 0x00, 0x00, 0x02, 0x00, 0x05, 0x00, 0x01, 0x02, 0xAA, 0xBB], pdu);
    }

    [Fact]
    public void BuildMaskWriteRequest_Packs()
    {
        Assert.Equal([0x00, 0x00, 0x00, 0xFF, 0x01, 0x00],
            ModbusRtuTransport.BuildMaskWriteRequest(0x00, 0x00FF, 0x0100));
    }

    [Fact]
    public async Task SendReceiveAsync_SendsFullAduWithCrc()
    {
        // Regression guard: the ADU buffer used to be allocated one byte short,
        // so the CRC low byte overwrote the PDU's last byte (0x25 became 0x4D)
        // and the CRC covered the wrong range — real devices rejected the frame.
        using var serial = OpenVirtual();
        using var transport = new ModbusRtuTransport(serial);

        var request = transport.SendReceiveAsync(0x01, 0x16, [0x00, 0x04, 0x00, 0xF2, 0x00, 0x25], 2000);
        serial.InjectRxData("01 16 00 04 00 F2 00 25 67 EE");
        await request;

        var sent = serial.GetSentData();
        var frame = Assert.Single(sent);
        Assert.Equal("0116000400F2002567EE", frame.RawHex);
    }

    [Fact]
    public void HexStringToBytes_ParsesSpacedHex()
    {
        Assert.Equal(new byte[] { 0x01, 0x02, 0xAA },
            ModbusRtuTransport.HexStringToBytes("01 02 AA"));
    }
}
