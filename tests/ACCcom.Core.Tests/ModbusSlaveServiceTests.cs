using System.Net.Sockets;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

[Collection("SerialTcp")]
public class ModbusSlaveServiceTests
{
    [Fact]
    public void GetDevice_null_or_empty_id_returns_null_without_throwing()
    {
        using var service = new ModbusSlaveService();
        var ex = Record.Exception(() => service.GetDevice(null!));
        Assert.Null(ex);
        Assert.Null(service.GetDevice(null!));
        Assert.Null(service.GetDevice(""));
        Assert.Null(service.GetDevice("ghost"));
    }

    [Fact]
    public void RemoveSlave_null_or_empty_id_is_safe_noop()
    {
        using var service = new ModbusSlaveService();
        var ex = Record.Exception(() => service.RemoveSlave(null!));
        Assert.Null(ex);
        service.RemoveSlave(null!);
        service.RemoveSlave("");
        Assert.Empty(service.GetActiveSlaves());
    }

    [Fact]
    public void CreateSlave_Tcp_RegistersAndLists()
    {
        using var service = new ModbusSlaveService();
        var id = service.CreateSlave(0x01, "tcp", TestPortHelper.GetFreePort().ToString());

        var slaves = service.GetActiveSlaves().ToList();
        var info = Assert.Single(slaves);
        Assert.Equal(id, info.Id);
        Assert.Equal(0x01, info.SlaveId);
        Assert.Equal("tcp", info.TransportType);
        Assert.True(info.IsRunning);
    }

    [Fact]
    public void CreateSlave_Rtu_UnopenablePort_ThrowsAndRegistersNothing()
    {
        // The Open result used to be discarded, so this registered a zombie
        // slave that could never answer — surfacing only as unexplained
        // timeouts on the master's side. (Open retries twice with 500ms
        // delays, so this takes ~1s.)
        using var service = new ModbusSlaveService();
        var ex = Assert.Throws<ArgumentException>(
            () => service.CreateSlave(0x01, "rtu", "COM_DOES_NOT_EXIST_XYZ"));

        Assert.Contains("COM_DOES_NOT_EXIST_XYZ", ex.Message);
        Assert.Empty(service.GetActiveSlaves());
    }

    [Fact]
    public void CreateSlave_NullOrEmptyTransport_Throws()
    {
        // A null transportType used to NRE at ToLowerInvariant before reaching
        // the switch's unsupported-transport arm.
        using var service = new ModbusSlaveService();
        Assert.Throws<ArgumentException>(() => service.CreateSlave(0x01, null!, "COM1"));
        Assert.Throws<ArgumentException>(() => service.CreateSlave(0x01, "", "COM1"));
    }

    [Fact]
    public void CreateSlave_UnsupportedTransport_Throws()
    {
        using var service = new ModbusSlaveService();
        Assert.Throws<ArgumentException>(() => service.CreateSlave(0x01, "can", "COM1"));
    }

    [Fact]
    public void CreateSlave_InvalidTcpPort_Throws()
    {
        using var service = new ModbusSlaveService();
        Assert.Throws<ArgumentException>(() => service.CreateSlave(0x01, "tcp", "not-a-port"));
        Assert.Throws<ArgumentException>(() => service.CreateSlave(0x01, "tcp", "0"));
    }

    [Fact]
    public void WriteAndReadRegister_RoundTrips()
    {
        using var service = new ModbusSlaveService();
        var id = service.CreateSlave(0x01, "tcp", TestPortHelper.GetFreePort().ToString());

        service.WriteRegister(id, RegisterType.HoldingRegister, 3, 0xABCD);
        Assert.Equal(0xABCD, service.ReadRegister(id, RegisterType.HoldingRegister, 3));

        service.WriteRegister(id, RegisterType.Coil, 7, 1);
        Assert.Equal(1, service.ReadRegister(id, RegisterType.Coil, 7));

        service.WriteRegister(id, RegisterType.InputRegister, 1, 0x1234);
        Assert.Equal(0x1234, service.ReadRegister(id, RegisterType.InputRegister, 1));

        service.WriteRegister(id, RegisterType.DiscreteInput, 2, 1);
        Assert.Equal(1, service.ReadRegister(id, RegisterType.DiscreteInput, 2));
    }

    [Fact]
    public async Task TcpSlave_ServesReadHoldingRegistersOverWire()
    {
        var port = TestPortHelper.GetFreePort();
        using var service = new ModbusSlaveService();
        service.CreateSlave(0x01, "tcp", port.ToString());
        service.WriteRegister("slave_1", RegisterType.HoldingRegister, 0, 0x1234);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();
        // MBAP (6) + unit (1) + FC03 read holding 1 register starting at 0.
        var req = new byte[] { 0x00, 0x02, 0x00, 0x00, 0x00, 0x06, 0x01, 0x03, 0x00, 0x00, 0x00, 0x01 };
        await stream.WriteAsync(req);

        var header = new byte[6];
        var read = await ReadFullyAsync(stream, header, TimeSpan.FromSeconds(3));
        Assert.Equal(6, read);
        Assert.Equal(0x00, header[2]); Assert.Equal(0x00, header[3]);
        var bodyLen = (header[4] << 8) | header[5];
        Assert.Equal(5, bodyLen); // unit + func + bytecount + 2 data bytes

        var body = new byte[bodyLen];
        read = await ReadFullyAsync(stream, body, TimeSpan.FromSeconds(3));
        Assert.Equal(bodyLen, read);
        Assert.Equal(0x01, body[0]); // unit id
        Assert.Equal(0x03, body[1]); // function code
        Assert.Equal(0x02, body[2]); // byte count
        Assert.Equal(0x12, body[3]);
        Assert.Equal(0x34, body[4]);
    }

    [Fact]
    public async Task TcpSlave_DropsMalformedLengthFrame_AndKeepsServing()
    {
        var port = TestPortHelper.GetFreePort();
        using var service = new ModbusSlaveService();
        service.CreateSlave(0x01, "tcp", port.ToString());
        service.WriteRegister("slave_1", RegisterType.HoldingRegister, 0, 0x1234);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();

        // MBAP header declaring a 65535-byte body — above the 254 protocol
        // maximum. The old code would have blocked the loop waiting for 64 KB
        // of body bytes (or allocated them); now the frame is dropped.
        await stream.WriteAsync(new byte[] { 0x00, 0x09, 0x00, 0x00, 0xFF, 0xFF });

        var probe = new byte[1];
        Assert.Equal(0, await ReadWithTimeoutAsync(stream, probe, TimeSpan.FromMilliseconds(500)));

        // The discriminating half: had the slave consumed the next request as
        // body bytes of the malformed frame, nothing would come back here.
        var req = new byte[] { 0x00, 0x0A, 0x00, 0x00, 0x00, 0x06, 0x01, 0x03, 0x00, 0x00, 0x00, 0x01 };
        await stream.WriteAsync(req);

        var header = new byte[6];
        Assert.Equal(6, await ReadFullyAsync(stream, header, TimeSpan.FromSeconds(3)));
        var bodyLen = (header[4] << 8) | header[5];
        var body = new byte[bodyLen];
        Assert.Equal(bodyLen, await ReadFullyAsync(stream, body, TimeSpan.FromSeconds(3)));
        Assert.Equal(0x34, body[^1]);
    }

    [Fact]
    public void RemoveSlave_DisposesTransport()
    {
        using var service = new ModbusSlaveService();
        var id = service.CreateSlave(0x01, "tcp", TestPortHelper.GetFreePort().ToString());
        Assert.Single(service.GetActiveSlaves());

        service.RemoveSlave(id);
        Assert.Empty(service.GetActiveSlaves());
    }

    [Fact]
    public void Dispose_ReleasesAllSlaves()
    {
        var service = new ModbusSlaveService();
        service.CreateSlave(0x01, "tcp", TestPortHelper.GetFreePort().ToString());
        service.CreateSlave(0x02, "tcp", TestPortHelper.GetFreePort().ToString());
        Assert.Equal(2, service.GetActiveSlaves().Count());

        service.Dispose();
        Assert.Empty(service.GetActiveSlaves());
    }

    [Fact]
    public void ReadRegister_UnknownSlave_ReturnsZero()
    {
        using var service = new ModbusSlaveService();
        Assert.Equal(0, service.ReadRegister("nope", RegisterType.HoldingRegister, 0));
    }

    private static async Task<int> ReadFullyAsync(NetworkStream stream, byte[] buffer, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cts.Token);
            if (read == 0) break;
            offset += read;
        }
        return offset;
    }

    /// <summary>Single bounded read: 0 means "nothing arrived within the
    /// timeout" (the cancellation surfaces as an exception, not a short read).</summary>
    private static async Task<int> ReadWithTimeoutAsync(NetworkStream stream, byte[] buffer, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            return await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
    }
}
