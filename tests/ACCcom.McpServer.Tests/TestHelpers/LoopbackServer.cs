using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ACCcom.McpServer.Tests.TestHelpers;

/// <summary>Loopback TCP peer for tool-layer TCP endpoint tests — same role
/// as ACCcom.Core.Tests' LoopbackServer, trimmed to what the tool tests use
/// (accept + per-connection packet log + echo). Test-project-local: the two
/// test assemblies do not reference each other.</summary>
internal sealed class LoopbackServer : IDisposable
{
    private sealed class Connection
    {
        public required TcpClient Client;
        public List<byte[]> Packets { get; } = new();
    }

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly List<Connection> _connections = new();

    public bool AutoEcho { get; set; }

    public int Port { get; }

    public LoopbackServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (true)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                var conn = new Connection { Client = client };
                lock (_gate) _connections.Add(conn);
                _ = Task.Run(() => ReadLoopAsync(conn));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private async Task ReadLoopAsync(Connection conn)
    {
        var stream = conn.Client.GetStream();
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length, _cts.Token).ConfigureAwait(false);
                if (read == 0) break;
                lock (_gate) conn.Packets.Add(buffer[..read]);
                if (AutoEcho)
                    await stream.WriteAsync(buffer.AsMemory(0, read), _cts.Token).ConfigureAwait(false);
            }
        }
        catch { /* client gone */ }
    }

    public int ConnectionCount { get { lock (_gate) return _connections.Count; } }

    public byte[]? Packet(int connectionIndex, int packetIndex)
    {
        lock (_gate)
        {
            if (_connections.Count <= connectionIndex) return null;
            var packets = _connections[connectionIndex].Packets;
            return packets.Count > packetIndex ? packets[packetIndex] : null;
        }
    }

    public string PacketText(int connectionIndex, int packetIndex)
        => Packet(connectionIndex, packetIndex) is { } bytes ? Encoding.UTF8.GetString(bytes) : "";

    public async Task<bool> WaitPacketAsync(int connectionIndex, int packetIndex, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (Packet(connectionIndex, packetIndex) != null) return true;
            await Task.Delay(15).ConfigureAwait(false);
        }
        return Packet(connectionIndex, packetIndex) != null;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        lock (_gate) foreach (var conn in _connections) { try { conn.Client.Close(); } catch { } }
        _cts.Dispose();
    }
}
