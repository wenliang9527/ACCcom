using System.Net;
using System.Net.Sockets;

namespace ACCcom.Core.Services;

public class ModbusTcpSlaveTransport : IDisposable
{
    private readonly int _port;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly List<TcpClient> _clients = new();
    private readonly object _lock = new();
    private bool _disposed;
    private bool _isRunning;

    public bool IsRunning => _isRunning;
    public int ConnectedClients { get { lock (_lock) return _clients.Count; } }
    public Func<byte, byte[], byte[]>? OnRequestReceived { get; set; }

    public ModbusTcpSlaveTransport(int port) { _port = port; }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _isRunning = false;
        _cts?.Cancel();
        _listener?.Stop();
        lock (_lock) { foreach (var c in _clients) { try { c.Dispose(); } catch { } } _clients.Clear(); }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                lock (_lock) _clients.Add(client);
                _ = HandleClientAsync(client, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            var stream = client.GetStream();
            var headerBuf = new byte[6];
            while (!ct.IsCancellationRequested)
            {
                var offset = 0;
                while (offset < 6)
                { var read = await stream.ReadAsync(headerBuf.AsMemory(offset, 6 - offset), ct).ConfigureAwait(false); if (read == 0) return; offset += read; }
                var tidHi = headerBuf[0]; var tidLo = headerBuf[1];
                var remaining = (headerBuf[4] << 8) | headerBuf[5];
                if (remaining < 2) continue;
                var body = new byte[remaining]; offset = 0;
                while (offset < remaining)
                { var read = await stream.ReadAsync(body.AsMemory(offset, remaining - offset), ct).ConfigureAwait(false); if (read == 0) return; offset += read; }
                var slaveId = body[0]; var pdu = body[1..];
                var handler = OnRequestReceived;
                if (handler == null) continue;
                byte[] responsePdu;
                try { responsePdu = handler(slaveId, pdu); }
                catch { continue; }
                if (responsePdu.Length == 0) continue;
                var respLen = 1 + responsePdu.Length;
                var resp = new byte[6 + respLen];
                resp[0] = tidHi; resp[1] = tidLo; resp[2] = 0; resp[3] = 0;
                resp[4] = (byte)(respLen >> 8); resp[5] = (byte)respLen;
                resp[6] = slaveId;
                Array.Copy(responsePdu, 0, resp, 7, responsePdu.Length);
                await stream.WriteAsync(resp, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally { lock (_lock) _clients.Remove(client); try { client.Dispose(); } catch { } }
    }

    public void Dispose() { if (_disposed) return; _disposed = true; Stop(); }
}
