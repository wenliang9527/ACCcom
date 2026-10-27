using System.Runtime.CompilerServices;
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

    [Theory]
    [InlineData(0, "9600")]    // None: plain digits = 8N1
    [InlineData(1, "9600O")]   // Odd suffix
    [InlineData(2, "9600E")]   // Even suffix — the JJ400 19200 8E1 case
    public async Task Open_ParitySuffix_IsExactHandshakeBytes_NoNewline(int parity, string expected)
    {
        using var service = new TcpSerialService();
        Assert.True(await service.OpenAsync(new SerialConfig
        {
            PortName = $"tcp://127.0.0.1:{_server.Port}",
            BaudRate = 9600,
            Parity = parity,
            Reconnect = new ReconnectSettings { ReconnectIntervalMs = 50 }
        }));

        Assert.True(await _server.WaitPacketAsync(0, 0), "handshake not received");
        var packet = _server.Packet(0, 0)!;
        // Exact bytes: the bridge parses the WHOLE first packet; an inline
        // terminator would fail its digit/length check and leak to the target.
        Assert.Equal(expected, Encoding.UTF8.GetString(packet));
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

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(service.SetBaudRate(9600));
        sw.Stop();
        // The reconnect grace is part of the contract (ReconnectGraceMs): it
        // keeps the teardown FIN ahead of the new SYN on the wireless hop.
        // Lower bound only — Task.Delay never fires early.
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(TcpSerialService.ReconnectGraceMs - 50),
            $"SetBaudRate returned in {sw.ElapsedMilliseconds}ms — reconnect grace missing?");

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

    [Fact]
    public async Task Dispose_WithoutPriorClose_ReleasesSocket()
    {
        var service = new TcpSerialService();
        Assert.True(await service.OpenAsync(Config()));

        service.Dispose(); // the skip-Close abandon path must still free the socket

        Assert.False(service.IsOpen);
        Assert.True(await _server.WaitDisconnectAsync(0), "Dispose left the socket open");
    }

    /// <summary>Empirical pin of the runtime's rooting behavior: an instance
    /// with a pending read is kept alive by the in-flight socket operation
    /// (documented on the finalizer), so dropping the last reference does
    /// NOT collect it and the finalizer does NOT run. If this ever flips
    /// (runtime change / read-loop restructure), the finalizer must be
    /// re-audited — until then the socket-release guarantee for connected
    /// instances rests on explicit Close/Dispose paths alone.</summary>
    [Fact]
    public async Task LeakedConnectedInstance_IsRootedByPendingRead_NotFinalized()
    {
        var weak = CreateLeakedConnectedService(Config());
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        Assert.True(weak.IsAlive, "instance was collected despite a pending read — finalizer contract changed, re-audit ~TcpSerialService");

        // However it was created, the leaked instance must not linger as a
        // SECOND connection: it never re-dialed, and it dies with the test's
        // server disposal.
        await Task.Delay(150);
        Assert.Equal(1, _server.ConnectionCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateLeakedConnectedService(SerialConfig config)
    {
        var service = new TcpSerialService();
        service.OpenAsync(config).GetAwaiter().GetResult();
        return new WeakReference(service);
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
