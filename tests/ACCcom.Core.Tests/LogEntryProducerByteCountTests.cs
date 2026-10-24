using System.Net;
using System.Net.Sockets;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

/// <summary>Every producer that knows its payload size at creation time must
/// carry it in <see cref="LogEntry.ByteCount"/> so consumers skip the hex
/// rescan in ResolvedByteCount. SerialService RX already did; these tests pin
/// the remaining producers (VirtualSerial TX/RX, network bridge TX/RX).</summary>
public class LogEntryProducerByteCountTests
{
    [Fact]
    public void VirtualSerial_Send_text_carries_utf8_byte_count()
    {
        using var serial = new VirtualSerialService();
        Assert.True(serial.Open(new SerialConfig { PortName = "COM_V", BaudRate = 115200 }));
        LogEntry? tx = null;
        serial.OnDataReceived += e => { if (e.Direction == "TX") tx = e; };

        Assert.True(serial.Send("AB", false));

        Assert.NotNull(tx);
        Assert.Equal(2, tx!.ByteCount);
        Assert.Equal(tx.ByteCount, tx.ResolvedByteCount());
    }

    [Fact]
    public void VirtualSerial_Send_hex_carries_decoded_byte_count()
    {
        using var serial = new VirtualSerialService();
        Assert.True(serial.Open(new SerialConfig { PortName = "COM_V", BaudRate = 115200 }));
        LogEntry? tx = null;
        serial.OnDataReceived += e => { if (e.Direction == "TX") tx = e; };

        Assert.True(serial.Send("AB CD", true));

        Assert.NotNull(tx);
        Assert.Equal(2, tx!.ByteCount);
        Assert.Equal(tx.ByteCount, tx.ResolvedByteCount());
    }

    [Fact]
    public void VirtualSerial_InjectRx_carries_byte_count()
    {
        using var serial = new VirtualSerialService();
        LogEntry? rx = null;
        serial.OnDataReceived += e => { if (e.Direction == "RX") rx = e; };

        serial.InjectRxData("4142");

        Assert.NotNull(rx);
        Assert.Equal(2, rx!.ByteCount);
        Assert.Equal(rx.ByteCount, rx.ResolvedByteCount());
    }

    [Fact]
    public async Task Bridge_send_and_receive_carry_byte_count()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var bridge = new NetworkBridgeService();
        var entries = new System.Collections.Concurrent.ConcurrentQueue<LogEntry>();
        bridge.OnDataReceived += entries.Enqueue;
        try
        {
            Assert.True(await bridge.ConnectTcp("127.0.0.1", port));

            // TX: the send itself produces the TX entry.
            Assert.True(bridge.Send("AB", false));
            var tx = entries.Single(e => e.Direction == "TX");
            Assert.Equal(2, tx.ByteCount);
            Assert.Equal(tx.ByteCount, tx.ResolvedByteCount());

            // RX: echo the same bytes back from the peer side.
            var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buf = new byte[16];
            var read = await stream.ReadAsync(buf.AsMemory(0, buf.Length));
            Assert.Equal(2, read);
            await stream.WriteAsync(buf.AsMemory(0, read));

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            LogEntry? rx = null;
            while (rx == null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
                rx = entries.FirstOrDefault(e => e.Direction == "RX");
            }
            Assert.NotNull(rx);
            Assert.Equal(2, rx!.ByteCount);
            Assert.Equal(rx.ByteCount, rx.ResolvedByteCount());
        }
        finally
        {
            bridge.Close();
            listener.Stop();
        }
    }
}
