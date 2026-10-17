using System.Net;
using System.Net.Sockets;
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

    [Fact]
    public async Task SendReceiveAsync_RoundTripsAgainstSlaveListener()
    {
        var port = TestPortHelper.GetFreePort();
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
        var port = TestPortHelper.GetFreePort();
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
    public async Task SendReceiveAsync_NonPositiveTimeout_ThrowsOperationCanceledNotArgument()
    {
        // A non-positive timeout used to throw ArgumentOutOfRangeException
        // inside CancellationTokenSource; it must behave as an immediate
        // timeout instead (OperationCanceledException).
        var port = TestPortHelper.GetFreePort();
        using var slave = new ModbusTcpSlaveTransport(port);
        slave.OnRequestReceived = (_, pdu) => new byte[0]; // never replies
        slave.Start();
        try
        {
            using var master = new ModbusTcpTransport("127.0.0.1", port);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await master.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 0));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await master.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], -100));
        }
        finally
        {
            slave.Stop();
        }
    }

    [Fact]
    public async Task SendReceiveAsync_ExceptionResponse_SurfacesAsException()
    {
        var port = TestPortHelper.GetFreePort();
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

    // ── Byte-exact MBAP framing (the R111 lesson, TCP side) ──

    // ── Reconnect isolation (the old receive loop read the _stream field and
    //    mass-failed every pending request on any loop exception) ──

    [Fact]
    public async Task Reconnect_AfterConnectionReset_ServesRequestsOnNewConnection()
    {
        // Contract: a hard-reset connection (the server closes while the
        // request is still unread → RST) must fail the request that was in
        // flight on it, and the transport must reconnect and serve the next
        // request cleanly. The receive loop must never fail requests that
        // belong to the reconnected socket — it used to read the shared
        // _stream field and, on any loop exception, fail EVERY pending
        // request.
        var port = TestPortHelper.GetFreePort();
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        using var master = new ModbusTcpTransport("127.0.0.1", port);
        try
        {
            // Connection 1: one clean exchange so the receive loop is running.
            // The request is started first — it is what opens the connection —
            // and only then does the fake server accept the socket.
            var t1 = master.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 3000);
            var c1 = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var req1 = new byte[12];
            await ReadExactAsync(c1.GetStream(), req1);
            await c1.GetStream().WriteAsync(ModbusTcpTransport.BuildMbap(
                (ushort)((req1[0] << 8) | req1[1]), 0x01, 0x03, [0x00, 0x2A]));
            Assert.Equal([0x01, 0x03, 0x00, 0x2A], await t1);

            // A request in flight when the connection is hard-reset must fail
            // (its own connection died) — but the transport must reconnect
            // and stay usable. Closing with the request bytes still unread
            // makes the OS send an RST, which is what production sees.
            var t2 = master.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x02], 2000);
            c1.Close();
            await Assert.ThrowsAnyAsync<Exception>(async () => await t2);

            // Connection 2 (automatic reconnect): a fresh request must
            // round-trip even though the old receive loop just died. The old
            // loop used to fail EVERY pending request on its way out, which
            // could take down requests that belonged to this new connection
            // (the loop read the shared _stream field instead of its own).
            var t3 = master.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x03], 3000);
            var c2 = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var req3 = new byte[12];
            await ReadExactAsync(c2.GetStream(), req3);
            await c2.GetStream().WriteAsync(ModbusTcpTransport.BuildMbap(
                (ushort)((req3[0] << 8) | req3[1]), 0x01, 0x03, [0x00, 0x2B]));
            Assert.Equal([0x01, 0x03, 0x00, 0x2B], await t3);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), timeout.Token);
            if (read == 0) throw new EndOfStreamException("fake server closed mid-frame");
            offset += read;
        }
    }

    [Fact]
    public void BuildMbap_ExactRequestFraming()
    {
        // 6-byte MBAP header + unit id + function code + PDU. The length field
        // is pdu.Length + 2 (unit id + function code), big-endian; a one-byte
        // short buffer would make the length lie and the PDU tail overwrite.
        var pdu = new byte[] { 0x00, 0x00, 0x00, 0x01 };
        var frame = ModbusTcpTransport.BuildMbap(0x1234, 0x01, 0x03, pdu);

        Assert.Equal(8 + pdu.Length, frame.Length);
        Assert.Equal([0x12, 0x34, 0x00, 0x00, 0x00, 0x06, 0x01, 0x03, 0x00, 0x00, 0x00, 0x01], frame);
    }

    [Fact]
    public void BuildResponseFrame_ExactResponseFraming()
    {
        // MBAP header echoes the transaction id, then [unit][func][data...].
        var resp = ModbusTcpSlaveTransport.BuildResponseFrame(0xAB, 0xCD, 0x02, new byte[] { 0x03, 0x00, 0x2A });

        Assert.Equal(6 + 1 + 3, resp.Length);
        Assert.Equal([0xAB, 0xCD, 0x00, 0x00, 0x00, 0x04, 0x02, 0x03, 0x00, 0x2A], resp);
    }
}
