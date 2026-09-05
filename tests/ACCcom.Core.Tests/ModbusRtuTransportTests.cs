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
    public async Task SendReceiveAsync_WithValidResponse_ReturnsPdu()
    {
        using var serial = OpenVirtual();
        using var transport = new ModbusRtuTransport(serial);

        // Slave echoes a valid FC03 response: [01 03 02 12 34] + CRC.
        _ = Task.Run(async () =>
        {
            await Task.Delay(30);
            var adu = new byte[] { 0x01, 0x03, 0x02, 0x12, 0x34 };
            var crc = CrcHelper.Crc16(adu);
            var frame = adu.Concat(new byte[] { (byte)(crc & 0xFF), (byte)(crc >> 8) }).ToArray();
            serial.InjectRxData(HexHelper.BytesToHexSpaced(frame, 0, frame.Length));
        });

        var response = await transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 2000);

        Assert.Equal([0x01, 0x03, 0x02, 0x12, 0x34], response);
    }

    [Fact]
    public async Task SendReceiveAsync_ExceptionResponse_ReturnsExceptionFrame()
    {
        using var serial = OpenVirtual();
        using var transport = new ModbusRtuTransport(serial);

        // Slave replies with an exception: [01 83 02] + CRC (func | 0x80).
        _ = Task.Run(async () =>
        {
            await Task.Delay(30);
            var adu = new byte[] { 0x01, 0x83, 0x02 };
            var crc = CrcHelper.Crc16(adu);
            var frame = adu.Concat(new byte[] { (byte)(crc & 0xFF), (byte)(crc >> 8) }).ToArray();
            serial.InjectRxData(HexHelper.BytesToHexSpaced(frame, 0, frame.Length));
        });

        var response = await transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 2000);

        // Exception frames surface as the raw body (caller inspects func|0x80).
        Assert.Equal([0x01, 0x83, 0x02], response);
    }

    [Fact]
    public async Task SendReceiveAsync_BadCrc_Throws()
    {
        using var serial = OpenVirtual();
        using var transport = new ModbusRtuTransport(serial);

        _ = Task.Run(async () =>
        {
            await Task.Delay(30);
            // Wrong CRC on purpose.
            serial.InjectRxData("01 03 02 12 34 00 00");
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 2000));
        Assert.Contains("CRC", ex.Message);
    }

    [Fact]
    public async Task SendReceiveAsync_WrongSlaveId_NotMatched_TimesOut()
    {
        using var serial = OpenVirtual();
        using var transport = new ModbusRtuTransport(serial);

        _ = Task.Run(async () =>
        {
            await Task.Delay(30);
            // Response for slave 0x02, but we asked for slave 0x01.
            var adu = new byte[] { 0x02, 0x03, 0x02, 0x12, 0x34 };
            var crc = CrcHelper.Crc16(adu);
            var frame = adu.Concat(new byte[] { (byte)(crc & 0xFF), (byte)(crc >> 8) }).ToArray();
            serial.InjectRxData(HexHelper.BytesToHexSpaced(frame, 0, frame.Length));
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 300));
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
    public void HexStringToBytes_ParsesSpacedHex()
    {
        Assert.Equal(new byte[] { 0x01, 0x02, 0xAA },
            ModbusRtuTransport.HexStringToBytes("01 02 AA"));
    }
}
