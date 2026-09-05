using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class ModbusTcpTransportTests
{
    [Fact]
    public void Dispose_CleansUpResources()
    {
        var transport = new ModbusTcpTransport("localhost", 502);

        transport.Dispose();
        transport.Dispose();
    }

    [Fact]
    public void Crc16_CalculatesCorrectly()
    {
        var data = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x0A };
        var crc = CrcHelper.Crc16(data);

        Assert.True(crc > 0);
    }

    [Fact]
    public void Sum8_CalculatesCorrectly()
    {
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var sum = CrcHelper.Sum8(data);

        Assert.Equal((byte)0x0A, sum);
    }

    [Fact]
    public void Xor8_CalculatesCorrectly()
    {
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var xor = CrcHelper.Xor8(data);

        Assert.Equal((byte)(0x01 ^ 0x02 ^ 0x03 ^ 0x04), xor);
    }

    [Fact]
    public void Sum16_CalculatesCorrectly()
    {
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var sum = CrcHelper.Sum16(data);

        Assert.Equal((ushort)0x0A, sum);
    }

    // ── MBAP framing + request/response round-trip (against a real listener) ──

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public async Task SendReceiveAsync_RoundTripsAgainstSlaveListener()
    {
        var port = GetFreePort();
        using var slave = new ModbusTcpSlaveTransport(port);
        slave.OnRequestReceived = (slaveId, pdu) =>
        {
            // pdu[0] is the function code; echo it back with 2 bytes of data.
            Assert.Equal(0x03, pdu[0]);
            return new byte[] { 0x03, 0x00, 0x2A };
        };
        slave.Start();
        try
        {
            using var master = new ModbusTcpTransport("127.0.0.1", port);
            var response = await master.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 3000);

            // SendReceiveAsync returns the full response body: unit id + function
            // code + data (the MBAP length field covers unit+func+pdu).
            Assert.Equal([0x01, 0x03, 0x00, 0x2A], response);
        }
        finally
        {
            slave.Stop();
        }
    }

    [Fact]
    public async Task SendReceiveAsync_Timeout_ThrowsOperationCanceled()
    {
        var port = GetFreePort();
        using var slave = new ModbusTcpSlaveTransport(port);
        // Handler never replies.
        slave.OnRequestReceived = (_, pdu) => new byte[0];
        slave.Start();
        try
        {
            using var master = new ModbusTcpTransport("127.0.0.1", port);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await master.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 300));
        }
        finally
        {
            slave.Stop();
        }
    }

    [Fact]
    public async Task SendReceiveAsync_ExceptionResponse_SurfacesAsException()
    {
        var port = GetFreePort();
        using var slave = new ModbusTcpSlaveTransport(port);
        slave.OnRequestReceived = (_, pdu) => new byte[] { (byte)(pdu[0] | 0x80), 0x02 }; // exception 0x02
        slave.Start();
        try
        {
            using var master = new ModbusTcpTransport("127.0.0.1", port);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await master.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 3000));
            Assert.Contains("0x02", ex.Message);
        }
        finally
        {
            slave.Stop();
        }
    }
}
