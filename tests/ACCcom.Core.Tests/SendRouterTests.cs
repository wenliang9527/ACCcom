using System.Net;
using System.Net.Sockets;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

/// <summary>SendRouter routing contract: bridge-connected sends must NOT touch
/// the serial port, and disconnected bridges must fall back to it. Uses a real
/// loopback TCP listener (a connect alone completes the handshake into the
/// backlog, so no accept is needed) and VirtualSerialService as the serial
/// side.</summary>
public class SendRouterTests : IDisposable, IAsyncLifetime
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly NetworkBridgeService _bridge = new();
    private readonly VirtualSerialService _serial = new();

    public async Task InitializeAsync()
    {
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Assert.True(await _bridge.ConnectTcp("127.0.0.1", port));
    }

    public void Dispose()
    {
        _bridge.Dispose();
        _serial.Dispose();
        _listener.Stop();
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public void Bridge_connected_routes_to_bridge_not_serial()
    {
        // Serial side is CLOSED: a send that leaked to it would fail (and log a
        // TX entry); the bridge path succeeds — proving the routing.
        Assert.False(_serial.IsOpen);

        var sent = SendRouter.Send(_serial, _bridge, "AB", false);

        Assert.True(sent);
        Assert.Empty(_serial.GetSentData());
    }

    [Fact]
    public void Disconnected_bridge_falls_back_to_serial()
    {
        Assert.True(_bridge.Close());
        Assert.True(_serial.Open(new ACCcom.Core.Models.SerialConfig { PortName = "COM_VIRT", BaudRate = 115200 }));

        var sent = SendRouter.Send(_serial, _bridge, "AB", false);

        Assert.True(sent);
        var tx = Assert.Single(_serial.GetSentData());
        Assert.Equal("TX", tx.Direction);
        Assert.Equal("AB", tx.Text);
    }

    [Fact]
    public async Task Null_bridge_falls_back_to_serial()
    {
        Assert.True(_serial.Open(new ACCcom.Core.Models.SerialConfig { PortName = "COM_VIRT", BaudRate = 115200 }));

        Assert.True(await SendRouter.SendAsync(_serial, null, "CD", true));

        var tx = Assert.Single(_serial.GetSentData());
        Assert.Equal("TX", tx.Direction);
        // Hex sends log the canonical spaced form on every transport; "CD" is
        // one hex byte (0xCD), not the ASCII characters 'C''D'.
        Assert.Equal("CD", tx.RawHex);
    }
}
