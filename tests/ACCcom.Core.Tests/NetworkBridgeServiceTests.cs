using System.Net;
using System.Net.Sockets;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

[Collection("SerialTcp")]
public class NetworkBridgeServiceTests
{
    [Fact]
    public void Constructor_CreatesInstance()
    {
        // Arrange & Act
        using var service = new NetworkBridgeService();

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void IsConnected_InitiallyFalse()
    {
        // Arrange
        using var service = new NetworkBridgeService();

        // Act & Assert
        Assert.False(service.IsConnected);
    }

    [Fact]
    public async Task ConnectTcp_InvalidHost_ReturnsFalse()
    {
        // Arrange
        using var service = new NetworkBridgeService();
        var errorRaised = false;
        service.OnError += _ => errorRaised = true;

        // Act
        var result = await service.ConnectTcp("invalid.host.local", 9999);

        // Assert
        Assert.False(result);
        Assert.False(service.IsConnected);
        Assert.True(errorRaised);
    }

    [Fact]
    public void Dispose_WhenNotConnected_DoesNotThrow()
    {
        // Arrange
        var service = new NetworkBridgeService();

        // Act & Assert
        var exception = Record.Exception(() => service.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void Events_CanBeAttached()
    {
        // Arrange
        using var service = new NetworkBridgeService();

        // Act & Assert - attaching handlers should not throw
        service.OnDataReceived += (LogEntry _) => { };
        service.OnDisconnected += () => { };
        service.OnError += (string _) => { };
    }

    // ── Success paths (real loopback TCP echo server) ──

    /// <summary>Echoes any received bytes back to the client.</summary>
    private sealed class EchoTcpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly List<TcpClient> _clients = new();
        private readonly object _lock = new();

        public int Port { get; }

        public EchoTcpServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptLoopAsync();
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                    lock (_lock) _clients.Add(client);
                    _ = EchoAsync(client, _cts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        private static async Task EchoAsync(TcpClient client, CancellationToken token)
        {
            try
            {
                var stream = client.GetStream();
                var buffer = new byte[4096];
                while (!token.IsCancellationRequested)
                {
                    var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                    if (read == 0) return;
                    await stream.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            finally { client.Dispose(); }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            lock (_lock)
            {
                foreach (var c in _clients) c.Dispose();
                _clients.Clear();
            }
        }
    }

    [Fact]
    public async Task ConnectTcp_ToLoopbackEcho_ConnectsAndSends()
    {
        using var server = new EchoTcpServer();
        using var service = new NetworkBridgeService();

        var result = await service.ConnectTcp("127.0.0.1", server.Port);

        Assert.True(result);
        Assert.True(service.IsConnected);
        Assert.Equal(NetworkProtocol.TCP, service.Protocol);
        Assert.Equal("127.0.0.1", service.Host);
        Assert.Equal(server.Port, service.Port);

        // Send over the connected bridge; the echo server will reflect it back.
        var echoed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.OnDataReceived += e =>
        {
            if (e.Direction == "RX") echoed.TrySetResult(e.Text);
        };
        service.Send("hello-bridge", isHex: false);

        var text = await echoed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("hello-bridge", text);
    }

    [Fact]
    public async Task ConnectTcp_SendHex_RoundTripsRawBytes()
    {
        using var server = new EchoTcpServer();
        using var service = new NetworkBridgeService();

        Assert.True(await service.ConnectTcp("127.0.0.1", server.Port));

        var echoed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.OnDataReceived += e =>
        {
            if (e.Direction == "RX") echoed.TrySetResult(e.RawHex.Replace(" ", ""));
        };
        service.SendHex("DE AD BE EF");

        var hex = await echoed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("DEADBEEF", hex);
    }

    [Fact]
    public async Task SendHex_InvalidHex_DoesNotDisconnect()
    {
        // An un-sendable hex string (illegal chars) must fail the send WITHOUT
        // tearing down the link — the old path threw FormatException inside
        // the try, which called HandleDisconnect and killed the connection.
        using var server = new EchoTcpServer();
        using var service = new NetworkBridgeService();

        Assert.True(await service.ConnectTcp("127.0.0.1", server.Port));

        Assert.False(service.SendHex("ZZ ZZ"));
        Assert.True(service.IsConnected); // still connected after the bad send

        // The connection still works for a subsequent valid send.
        var echoed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.OnDataReceived += e =>
        {
            if (e.Direction == "RX") echoed.TrySetResult(e.RawHex.Replace(" ", ""));
        };
        service.SendHex("AA BB");

        var hex = await echoed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("AABB", hex);
    }

    [Fact]
    public async Task Send_WhenServerCloses_Disconnects()
    {
        // A server that accepts, reads one message, then closes the connection.
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var port = ((IPEndPoint)server.LocalEndpoint).Port;

        using var service = new NetworkBridgeService();
        Assert.True(await service.ConnectTcp("127.0.0.1", port));

        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.OnDisconnected += () => disconnected.TrySetResult();

        // Server accepts and immediately closes -> client read returns 0 -> disconnect.
        var client = await server.AcceptTcpClientAsync();
        client.Dispose();
        server.Stop();

        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(service.IsConnected);
    }

    [Fact]
    public async Task ConnectUdp_Loopback_ReceivesEcho()
    {
        // A UDP echo endpoint: bind a socket, connect the bridge, send, and
        // have the echo side reflect the datagram back.
        var echo = new UdpClient(0);
        var echoPort = ((IPEndPoint)echo.Client.LocalEndPoint!).Port;

        using var service = new NetworkBridgeService();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.OnDataReceived += e =>
        {
            if (e.Direction == "RX") received.TrySetResult(e.Text);
        };

        Assert.True(service.ConnectUdp("127.0.0.1", echoPort));
        Assert.True(service.IsConnected);
        Assert.Equal(NetworkProtocol.UDP, service.Protocol);

        service.Send("udp-ping", isHex: false);

        // Wait for the datagram on the echo socket, then bounce it back to the
        // sender (the bridge's bound socket).
        var from = await echo.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var pong = System.Text.Encoding.UTF8.GetBytes("udp-pong");
        await echo.SendAsync(pong, from.RemoteEndPoint);

        var text = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("udp-pong", text);
        echo.Dispose();
    }

    [Fact]
    public async Task Close_WhenConnected_DisconnectsCleanly()
    {
        using var server = new EchoTcpServer();
        using var service = new NetworkBridgeService();

        Assert.True(await service.ConnectTcp("127.0.0.1", server.Port));

        var result = service.Close();

        Assert.True(result);
        Assert.False(service.IsConnected);
        // Send after close must fail and raise an error.
        var errorRaised = false;
        service.OnError += _ => errorRaised = true;
        Assert.False(service.Send("after-close", isHex: false));
        Assert.True(errorRaised);
    }

    [Fact]
    public void Send_null_or_empty_returns_false_without_error()
    {
        using var service = new NetworkBridgeService();
        var errorRaised = false;
        service.OnError += _ => errorRaised = true;

        Assert.False(service.Send(null, isHex: false));
        Assert.False(service.Send("", isHex: false));
        // The null/empty guard fires before the "not connected" path, so no
        // error event is raised for an input that was never sendable.
        Assert.False(errorRaised);
    }

    [Fact]
    public async Task Send_null_while_connected_does_not_disconnect()
    {
        using var server = new EchoTcpServer();
        using var service = new NetworkBridgeService();
        Assert.True(await service.ConnectTcp("127.0.0.1", server.Port));

        var errorRaised = false;
        service.OnError += _ => errorRaised = true;

        // null must be rejected without tearing the connection down (the old
        // behavior threw inside the try and triggered HandleDisconnect).
        Assert.False(service.Send(null, isHex: false));
        Assert.True(service.IsConnected, "Connection must survive an invalid send input");
        Assert.False(errorRaised);
    }
}
