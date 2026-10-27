using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ACCcom.Core.Tests;

/// <summary>In-process TCP peer for TcpSerialService/EndpointSerialService
/// tests: accepts every connection, records each received chunk ("packet")
/// per connection in arrival order, can push bytes to the newest client,
/// close it (simulating a peer drop), or echo everything received
/// (AutoEcho). Loopback-only — no hardware, no real serial ports.</summary>
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

    /// <summary>When set, every received chunk is written straight back —
    /// turns the server into an echo peer for end-to-end send/wait tests.</summary>
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
        catch (OperationCanceledException) { /* disposed */ }
        catch (ObjectDisposedException) { /* listener stopped */ }
        catch (SocketException) { /* listener stopped */ }
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
        catch
        {
            // client gone — the connection's packets stay recorded
        }
    }

    public int ConnectionCount
    {
        get { lock (_gate) return _connections.Count; }
    }

    /// <summary>The recorded packet bytes, or null when not (yet) received.</summary>
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

    /// <summary>Polls until the packet exists or the deadline passes. Timing
    /// assertions poll instead of sleeping once (handshake/reconnect land on
    /// background threads).</summary>
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

    public async Task<bool> WaitConnectionsAsync(int count, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (ConnectionCount >= count) return true;
            await Task.Delay(15).ConfigureAwait(false);
        }
        return ConnectionCount >= count;
    }

    public void SendToCurrent(byte[] payload)
    {
        TcpClient? client;
        lock (_gate) client = _connections.Count > 0 ? _connections[^1].Client : null;
        try { client?.GetStream().Write(payload, 0, payload.Length); }
        catch { /* current client already gone */ }
    }

    /// <summary>Drops the newest client the way a dead peer would — the
    /// client side sees a normal connection loss, not a graceful close by
    /// the service.</summary>
    public void CloseCurrentClient()
    {
        TcpClient? client;
        lock (_gate) client = _connections.Count > 0 ? _connections[^1].Client : null;
        try { client?.Close(); } catch { /* already gone */ }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { /* already stopped */ }
        lock (_gate)
        {
            foreach (var conn in _connections)
            {
                try { conn.Client.Close(); } catch { /* already gone */ }
            }
        }
        _cts.Dispose();
    }
}
