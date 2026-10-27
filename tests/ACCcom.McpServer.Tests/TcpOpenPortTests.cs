using System.Text.Json;
using ACCcom.Core.Services;
using ACCcom.McpServer.Tests.TestHelpers;
using ACCcom.McpServer.Tools;
using Xunit;

namespace ACCcom.McpServer.Tests;

/// <summary>open_port/send/send_and_wait over a TCP endpoint, through the
/// real SerialTools + ToolContext wiring (the dispatcher registered exactly
/// as Program.cs registers it) against a loopback peer. This is the layer
/// that proves an agent can drive the wireless UART bridge with zero serial
/// hardware attached. Exclusive collection: live sockets + timing-sensitive
/// handshakes destabilize marginal tests (and these tests) when run inside
/// the parallel pool — see the Core suite's TcpSerialCollection note.</summary>
[Collection("tcp-tools")]
public class TcpOpenPortTests : IDisposable
{
    private readonly LoopbackServer _server = new() { AutoEcho = true };
    private readonly string _tempTrafficPath = Path.Combine(Path.GetTempPath(), $"acccom-tcp-test-{Guid.NewGuid():N}.jsonl");
    private readonly ToolContext _ctx;
    private readonly MultiPortService _multiPort;

    public TcpOpenPortTests()
    {
        _multiPort = new MultiPortService(() => new EndpointSerialService());
        _ctx = new ToolContext(_multiPort, new EndpointSerialService())
        {
            TrafficLog = new McpTrafficLog(_tempTrafficPath)
        };
    }

    private string Endpoint => $"tcp://127.0.0.1:{_server.Port}";

    [Fact]
    public async Task OpenPort_CanonicalTcpUrl_OpensAndEchoesHandshakeOnWire()
    {
        var tools = new SerialTools(_ctx);
        var result = await tools.OpenPort(Endpoint, baudRate: 57600);

        Assert.True(ToolContextFactory.ExtractSuccess(result));
        using var doc = JsonDocument.Parse(result);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(Endpoint, data.GetProperty("port").GetString());
        Assert.Equal(57600, data.GetProperty("baudRate").GetInt32());

        Assert.True(await _server.WaitPacketAsync(0, 0), "handshake not received");
        Assert.Equal("57600", _server.PacketText(0, 0));
    }

    [Fact]
    public async Task OpenPort_BareHostPort_AcceptedAndResponseNormalized()
    {
        var tools = new SerialTools(_ctx);
        var result = await tools.OpenPort($"127.0.0.1:{_server.Port}");

        Assert.True(ToolContextFactory.ExtractSuccess(result));
        using var doc = JsonDocument.Parse(result);
        // The response reports the endpoint the service opened — the
        // normalized canonical form, not the bare input spelling.
        Assert.Equal(Endpoint, doc.RootElement.GetProperty("data").GetProperty("port").GetString());
        var close = await tools.ClosePort();
        Assert.True(ToolContextFactory.ExtractSuccess(close));
    }

    [Fact]
    public async Task OpenPort_TcpBaudAboveBridgeCap_FailsInvalidConfig()
    {
        var tools = new SerialTools(_ctx);
        // Port 1: nothing listens — but the cap check must fail the call
        // before any dial attempt, so no connection is ever made.
        var result = await tools.OpenPort("tcp://127.0.0.1:1", baudRate: 2_000_000);

        Assert.False(ToolContextFactory.ExtractSuccess(result));
        Assert.Equal("INVALID_CONFIG", ToolContextFactory.ExtractErrorCode(result));
        Assert.Equal(0, _server.ConnectionCount);
    }

    [Fact]
    public async Task SendAndWait_OverTcp_EchoRoundTrip()
    {
        var tools = new SerialTools(_ctx);
        Assert.True(ToolContextFactory.ExtractSuccess(await tools.OpenPort(Endpoint)));

        var result = await tools.SendAndWait("PING", "PING", timeoutMs: 5000);

        Assert.True(ToolContextFactory.ExtractSuccess(result), result);
        using var doc = JsonDocument.Parse(result);
        var data = doc.RootElement.GetProperty("data");
        // matched=true proves the RX path (server echo → read loop → buffer →
        // match) works over the TCP transport end to end.
        Assert.True(data.GetProperty("matched").GetBoolean());
    }

    [Fact]
    public async Task OpenPort_TaggedTcpSession_OpensSendsCloses()
    {
        var tools = new SerialTools(_ctx);
        var open = await tools.OpenPort(Endpoint, tag: "w");
        Assert.True(ToolContextFactory.ExtractSuccess(open));
        using (var doc = JsonDocument.Parse(open))
        {
            var data = doc.RootElement.GetProperty("data");
            Assert.Equal(Endpoint, data.GetProperty("port").GetString());
            Assert.Equal("w", data.GetProperty("tag").GetString());
        }

        var send = await tools.Send("DATA", tag: "w");
        Assert.True(ToolContextFactory.ExtractSuccess(send));
        Assert.True(await _server.WaitPacketAsync(0, 1), "tagged payload not received");
        Assert.Equal("DATA", _server.PacketText(0, 1));

        var close = await tools.ClosePort("w");
        Assert.True(ToolContextFactory.ExtractSuccess(close));
        Assert.Null(_ctx.MultiPort.GetPort("w"));
    }

    [Fact]
    public async Task OpenPort_UnreachableTcp_FailsOpenFailed()
    {
        var tools = new SerialTools(_ctx);
        var result = await tools.OpenPort("tcp://127.0.0.1:1");

        Assert.False(ToolContextFactory.ExtractSuccess(result));
        Assert.Equal("OPEN_FAILED", ToolContextFactory.ExtractErrorCode(result));
    }

    public void Dispose()
    {
        _multiPort.Dispose();
        _ctx.Serial.Dispose();
        _server.Dispose();
        try { File.Delete(_tempTrafficPath); } catch { /* temp */ }
    }
}
