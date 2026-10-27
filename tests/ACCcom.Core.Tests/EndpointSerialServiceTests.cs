using System.Diagnostics;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

/// <summary>EndpointSerialService dispatch contract: a TCP port name opens
/// the TCP transport (verified end-to-end against the loopback peer — the
/// behavioral proof, no inner-type poking), pre-Open event subscribers keep
/// receiving after the inner service is created (MultiPortService wires
/// events at construction time), and a transport-kind switch between opens
/// swaps the inner service instead of misusing it.</summary>
[Collection("tcp-serial")]
public class EndpointSerialServiceTests : IDisposable
{
    private readonly LoopbackServer _server = new();

    [Fact]
    public async Task TcpPortName_OpensTcpTransport_EndToEnd()
    {
        using var service = new EndpointSerialService();
        // Bare host:port input — accepted and normalized on the CurrentPort echo.
        Assert.True(await service.OpenAsync(new SerialConfig { PortName = $"127.0.0.1:{_server.Port}", BaudRate = 38400 }));

        Assert.True(service.IsOpen);
        Assert.Equal($"tcp://127.0.0.1:{_server.Port}", service.CurrentPort);

        // Wire proof: the handshake digits arrived at the TCP peer.
        Assert.True(await _server.WaitPacketAsync(0, 0), "handshake not received");
        Assert.Equal("38400", _server.PacketText(0, 0));

        Assert.True(service.Send("PING"));
        Assert.True(await _server.WaitPacketAsync(0, 1), "payload not received");
        Assert.Equal("PING", _server.PacketText(0, 1));
    }

    [Fact]
    public async Task EventsAttachedBeforeOpen_FireAfterInnerCreation()
    {
        using var service = new EndpointSerialService();
        var entries = new List<LogEntry>();
        var errors = new List<string>();
        // Subscribe BEFORE any Open — mirrors MultiPortService.CreateWiredService.
        service.OnDataReceived += e => { lock (entries) entries.Add(e); };
        service.OnError += m => { lock (errors) errors.Add(m); };

        Assert.True(await service.OpenAsync(new SerialConfig { PortName = $"tcp://127.0.0.1:{_server.Port}" }));
        _server.SendToCurrent(new byte[] { 0x41 });

        var rxSeen = false;
        var deadline = Environment.TickCount64 + 3000;
        while (Environment.TickCount64 < deadline)
        {
            lock (entries) if (entries.Any(e => e.Direction == "RX" && e.Text == "A")) { rxSeen = true; break; }
            await Task.Delay(15);
        }
        Assert.True(rxSeen, "RX entry did not reach a pre-Open subscriber");
        lock (entries) Assert.Contains(entries, e => e.Direction == "TX" && e.Text == "115200"); // handshake TX forwarded too
    }

    [Fact]
    public async Task TransportSwitchBetweenOpens_ReplacesInner()
    {
        using var service = new EndpointSerialService();
        Assert.True(await service.OpenAsync(new SerialConfig { PortName = $"tcp://127.0.0.1:{_server.Port}" }));
        Assert.True(service.IsOpen);
        Assert.True(service.Close());
        Assert.False(service.IsOpen);

        // Switch to a COM name: nothing with that name exists on the test
        // machine, so the open fails — but it must fail as a SERIAL open
        // (SerialService retry, no TCP dial), i.e. the dispatch swapped.
        var sw = Stopwatch.StartNew();
        Assert.False(service.Open(new SerialConfig { PortName = "COM_INVALID_X", BaudRate = 115200 }));
        sw.Stop();
        Assert.False(service.IsOpen);
        // SerialService retries 2× with 500ms sleeps; a refused TCP loopback
        // dial would also retry, so timing alone cannot discriminate — the
        // CurrentPort staying null and no new server connection does.
        Assert.Equal(1, _server.ConnectionCount); // nobody dialed the loopback
    }

    [Fact]
    public async Task MultiPort_WithDispatchingFactory_TcpTagWorks()
    {
        using var multiPort = new MultiPortService(() => new EndpointSerialService());
        var tagged = new List<LogEntry>();
        multiPort.OnDataReceived += e => { lock (tagged) tagged.Add(e); };

        var config = new SerialConfig { PortName = $"tcp://127.0.0.1:{_server.Port}", BaudRate = 115200 };
        Assert.True(await multiPort.OpenPortAsync("w", config));

        Assert.True(multiPort.SendToPort("w", "DATA"));
        Assert.True(await _server.WaitPacketAsync(0, 1), "tagged send not received");
        Assert.Equal("DATA", _server.PacketText(0, 1));

        _server.SendToCurrent(new byte[] { 0x7F });
        var rxSeen = false;
        var deadline = Environment.TickCount64 + 3000;
        while (Environment.TickCount64 < deadline)
        {
            lock (tagged) if (tagged.Any(e => e.Direction == "RX" && e.PortTag == "w")) { rxSeen = true; break; }
            await Task.Delay(15);
        }
        Assert.True(rxSeen, "tagged RX entry missing or untagged");

        multiPort.ClosePort("w");
    }

    [Fact]
    public async Task NullConfig_FailsCleanly_AllPaths()
    {
        using var service = new EndpointSerialService();
        Assert.False(service.Open(null));
        Assert.False(await service.OpenAsync(null));
        Assert.False(service.Send("x"));
        Assert.True(service.Close());       // nothing open — vacuous success
        Assert.False(service.SetBaudRate(9600));
    }

    public void Dispose() => _server.Dispose();
}
