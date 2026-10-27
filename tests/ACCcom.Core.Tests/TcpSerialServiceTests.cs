using System.Text;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

/// <summary>TcpSerialService contract against a loopback TCP peer — the
/// windowsair bridge semantics (first-packet baud command, single client,
/// reconnect behavior) are exercised without hardware: the loopback server
/// plays the bridge, and its per-connection packet log is the assertion
/// surface for "what actually went on the wire".</summary>
[Collection("tcp-serial")]
public class TcpSerialServiceTests : IDisposable
{
    private readonly LoopbackServer _server = new();

    private SerialConfig Config(int baud = 115200, int reconnectIntervalMs = 50) => new()
    {
        PortName = $"tcp://127.0.0.1:{_server.Port}",
        BaudRate = baud,
        Reconnect = new ReconnectSettings { ReconnectIntervalMs = reconnectIntervalMs }
    };

    [Fact]
    public async Task Open_SendsBaudDigitsAsFirstPacket_AndNormalizesPort()
    {
        using var service = new TcpSerialService();
        Assert.True(await service.OpenAsync(Config(baud: 9600)));

        Assert.True(service.IsOpen);
        Assert.Equal($"tcp://127.0.0.1:{_server.Port}", service.CurrentPort);
        Assert.Equal(9600, service.BaudRate);

        // The handshake is the FIRST packet — pure ASCII digits, no terminator
        // (a trailing newline would fail the bridge's digit-only parse).
        Assert.True(await _server.WaitPacketAsync(0, 0), "no first packet received");
        Assert.Equal("9600", _server.PacketText(0, 0));
    }

    [Fact]
    public async Task Open_HandshakeIsAuditedAsTxEntry()
    {
        using var service = new TcpSerialService();
        var entries = new List<LogEntry>();
        service.OnDataReceived += e => { lock (entries) entries.Add(e); };

        Assert.True(await service.OpenAsync(Config()));

        var handshake = await PollAsync(() => { lock (entries) return entries.Any(e => e.Direction == "TX" && e.Text == "115200"); });
        Assert.True(handshake, "handshake TX entry not dispatched");
        lock (entries)
        {
            var tx = entries.Single(e => e.Direction == "TX");
            Assert.Equal(6, tx.ByteCount);
            Assert.Equal("31 31 35 32 30 30", tx.RawHex); // ASCII "115200"
        }
    }

    [Fact]
    public async Task Send_TextAndHex_ReachServer_AsWholePackets()
    {
        using var service = new TcpSerialService();
        var entries = new List<LogEntry>();
        service.OnDataReceived += e => { lock (entries) entries.Add(e); };

        Assert.True(service.Open(Config())); // sync open path
        Assert.True(service.Send("hello"));
        Assert.True(service.Send("AB CD", isHex: true));

        Assert.True(await _server.WaitPacketAsync(0, 2), "text packet not received");
        Assert.Equal("hello", _server.PacketText(0, 1));
        Assert.Equal(new byte[] { 0xAB, 0xCD }, _server.Packet(0, 2));

        var txs = await PollAsync(() => { lock (entries) return entries.Count(e => e.Direction == "TX") >= 3; });
        Assert.True(txs, "TX entries missing");
        lock (entries)
        {
            var text = entries.First(e => e.Direction == "TX" && e.Text == "hello");
            Assert.Equal(5, text.ByteCount);
            var hex = entries.First(e => e.Direction == "TX" && e.Text == "AB CD");
            Assert.Equal(2, hex.ByteCount);
        }
    }

    [Fact]
    public async Task Send_EmptyOrInvalid_FailsCleanly()
    {
        using var service = new TcpSerialService();
        Assert.False(service.Send(""));            // silent rejection
        Assert.False(service.Send(null));          // null guard
        Assert.False(service.Send("ZZ", isHex: true)); // malformed hex, no wire bytes

        Assert.True(await service.OpenAsync(Config()));
        Assert.False(service.Send("GG", isHex: true));
        Assert.True(await _server.WaitPacketAsync(0, 0));
        Assert.Equal("115200", _server.PacketText(0, 0));
        Assert.Null(_server.Packet(0, 1)); // nothing but the handshake went out
    }

    [Fact]
    public async Task Receive_BytesBecomeRxEntries()
    {
        using var service = new TcpSerialService();
        var entries = new List<LogEntry>();
        service.OnDataReceived += e => { lock (entries) entries.Add(e); };
        Assert.True(await service.OpenAsync(Config()));

        // One send, one awaited entry at a time: two back-to-back sends can
        // coalesce into a single TCP read (Nagle + loopback), and asserting
        // per-chunk boundaries on a stream transport would be testing TCP,
        // not the service.
        _server.SendToCurrent(new byte[] { 0x41, 0x42 });
        var ab = await PollAsync(() => { lock (entries) return entries.Any(e => e.Direction == "RX" && e.Text == "AB"); });
        Assert.True(ab, "ASCII RX entry missing");
        lock (entries)
        {
            var entry = entries.First(e => e.Direction == "RX" && e.Text == "AB");
            Assert.Equal("41 42", entry.RawHex);
            Assert.Equal(2, entry.ByteCount);
        }

        _server.SendToCurrent(Encoding.UTF8.GetBytes("你好"));
        var cjk = await PollAsync(() => { lock (entries) return entries.Any(e => e.Direction == "RX" && e.Text == "你好"); });
        Assert.True(cjk, "CJK RX entry missing");
        lock (entries)
        {
            var entry = entries.First(e => e.Direction == "RX" && e.Text == "你好");
            Assert.Equal(6, entry.ByteCount); // UTF-8 bytes, not chars
        }
    }

    [Fact]
    public async Task SetBaudRate_ReconnectsAndRehandshakesNewBaud()
    {
        using var service = new TcpSerialService();
        Assert.True(await service.OpenAsync(Config(baud: 115200)));
        Assert.True(await _server.WaitPacketAsync(0, 0), "initial handshake not received");
        Assert.Equal("115200", _server.PacketText(0, 0));

        Assert.True(service.SetBaudRate(9600));

        // Transparent reconnect: a second connection whose FIRST packet
        // carries the new rate; the session stays open throughout.
        Assert.True(await _server.WaitConnectionsAsync(2), "no second connection");
        Assert.True(await _server.WaitPacketAsync(1, 0), "no re-handshake packet");
        Assert.Equal("9600", _server.PacketText(1, 0));
        Assert.True(service.IsOpen);
        Assert.Equal(9600, service.BaudRate);
        Assert.Equal(9600, service.ActiveConfig!.BaudRate); // synced: a later reconnect re-asserts 9600
    }

    [Fact]
    public async Task SetBaudRate_Guards_ReturnFalse()
    {
        using var service = new TcpSerialService();
        Assert.False(service.SetBaudRate(115200)); // not connected
        Assert.True(await service.OpenAsync(Config()));
        Assert.False(service.SetBaudRate(0));      // invalid rate
        Assert.True(service.IsOpen);               // untouched by the rejection
        Assert.Equal(1, _server.ConnectionCount);
    }

    [Fact]
    public async Task ServerDrop_RaisesDisconnected_AutoReconnectRehandshakes()
    {
        using var service = new TcpSerialService();
        var entries = new List<LogEntry>();
        service.OnDataReceived += e => { lock (entries) entries.Add(e); };
        var disconnected = 0;
        service.OnDisconnected += () => Interlocked.Increment(ref disconnected);

        Assert.True(await service.OpenAsync(Config(baud: 57600)));
        _server.CloseCurrentClient(); // peer dies mid-session

        Assert.True(await PollAsync(() => Volatile.Read(ref disconnected) >= 1), "OnDisconnected not raised");
        Assert.True(await _server.WaitConnectionsAsync(2), "auto reconnect did not re-dial");
        Assert.True(await _server.WaitPacketAsync(1, 0), "reconnect handshake missing");
        Assert.Equal("57600", _server.PacketText(1, 0)); // reconnect re-asserts the CURRENT baud
        Assert.True(service.IsOpen);
        var note = await PollAsync(() => { lock (entries) return entries.Any(e => e.Direction == "RX" && e.Text.Contains("[Auto reconnect]")); });
        Assert.True(note, "reconnect success note missing");
    }

    [Fact]
    public async Task Close_CancelsAutoReconnect()
    {
        using var service = new TcpSerialService();
        Assert.True(await service.OpenAsync(Config()));
        Assert.True(service.Close());

        Assert.False(service.IsOpen);
        Assert.Null(service.ActiveConfig);

        // No reconnect may fire after an explicit close.
        await Task.Delay(250);
        Assert.Equal(1, _server.ConnectionCount);
    }

    [Fact]
    public async Task Open_UnreachableEndpoint_FailsFalse()
    {
        using var service = new TcpSerialService();
        var errors = new List<string>();
        service.OnError += m => { lock (errors) errors.Add(m); };

        var config = new SerialConfig { PortName = "tcp://127.0.0.1:1", BaudRate = 115200 };
        Assert.False(await service.OpenAsync(config)); // port 1: nothing listens on loopback
        Assert.False(service.IsOpen);
        Assert.Contains(errors, m => m.Contains("Open failed"));
    }

    [Fact]
    public void Open_NonTcpPortName_FailsFast()
    {
        using var service = new TcpSerialService();
        // The dispatcher routes COM names elsewhere; a direct TcpSerialService
        // call with one must refuse instead of dialing garbage.
        Assert.False(service.Open(new SerialConfig { PortName = "COM3", BaudRate = 115200 }));
        Assert.False(service.IsOpen);
    }

    private static async Task<bool> PollAsync(Func<bool> condition, int timeoutMs = 3000, int intervalMs = 15)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition()) return true;
            await Task.Delay(intervalMs);
        }
        return condition();
    }

    public void Dispose() => _server.Dispose();
}

public class TcpEndpointTests
{
    // Grouped with the TCP suite (see TcpSerialCollection) though it is pure
    // parsing with no I/O.
    [Theory]
    [InlineData("tcp://192.168.0.125:1234", "192.168.0.125", 1234)]
    [InlineData("TCP://Host.Example:1234", "Host.Example", 1234)]   // scheme case-insensitive
    [InlineData("tcp://jj400:1234", "jj400", 1234)]                  // single-label hostname
    [InlineData("192.168.0.125:1234", "192.168.0.125", 1234)]        // bare host:port
    [InlineData("host_name-1.local:9999", "host_name-1.local", 9999)]
    [InlineData("tcp://[::1]:1234", "::1", 1234)]                    // bracketed IPv6
    [InlineData("  tcp://127.0.0.1:80  ", "127.0.0.1", 80)]          // surrounding whitespace
    public void TryParse_Accepts(string input, string expectedHost, int expectedPort)
    {
        Assert.True(TcpEndpoint.TryParse(input, out var endpoint));
        Assert.Equal(expectedHost, endpoint.Host);
        Assert.Equal(expectedPort, endpoint.Port);
    }

    [Theory]
    [InlineData("COM3")]                     // serial names never reach the TCP path
    [InlineData("COM3:")]                    // colon with no port
    [InlineData("")]
    [InlineData(null)]
    [InlineData("tcp://192.168.0.125")]      // port required
    [InlineData("tcp://192.168.0.125:")]     // empty port
    [InlineData("192.168.0.125:0")]          // port out of range
    [InlineData("192.168.0.125:65536")]
    [InlineData("192.168.0.125:123456")]     // >5 digits
    [InlineData("192.168.0.125:12a4")]       // non-digit port
    [InlineData("host:1234/path")]
    [InlineData("a b:1234")]
    [InlineData(":1234")]                    // empty host
    [InlineData("::1:1234")]                 // unbracketed IPv6
    [InlineData("tcp://user@host:1234")]     // userinfo residue
    [InlineData("tcp://[notv6]:1234")]       // brackets without an IPv6 literal
    public void TryParse_Rejects(string? input)
    {
        Assert.False(TcpEndpoint.TryParse(input, out _));
    }

    [Fact]
    public void NormalizedForm_IsCanonicalToString()
    {
        Assert.True(TcpEndpoint.TryParse("192.168.0.125:1234", out var bare));
        Assert.Equal("tcp://192.168.0.125:1234", bare.ToString());

        // The two spellings of one endpoint parse equal (record struct value
        // equality), so bare and canonical input land on the same session
        // identity in CurrentPort/list_open_ports.
        Assert.True(TcpEndpoint.TryParse("tcp://192.168.0.125:1234", out var canonical));
        Assert.Equal(canonical, bare);
    }
}
