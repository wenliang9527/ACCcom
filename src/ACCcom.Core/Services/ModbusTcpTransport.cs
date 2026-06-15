using System.Net.Sockets;
using Timer = System.Timers.Timer;

namespace ACCcom.Core.Services;

public class ModbusTcpTransport : IModbusTransport
{
    private readonly string _host;
    private readonly int _port;
    private TcpClient _client;
    private NetworkStream? _stream;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly Dictionary<ushort, TaskCompletionSource<byte[]>> _pending = new();
    private readonly CancellationTokenSource _receiveCts = new();
    private readonly Timer _heartbeatTimer;
    private int _transactionId;
    private bool _disposed;

    public ModbusTcpTransport(string host, int port)
    {
        _host = host;
        _port = port;
        _client = new TcpClient();
        _heartbeatTimer = new Timer(30_000);
        _heartbeatTimer.Elapsed += async (_, _) =>
        {
            try { await HeartbeatAsync(); }
            catch { }
        };
        _heartbeatTimer.AutoReset = true;
        _heartbeatTimer.Start();
    }

    public async Task<byte[]> SendReceiveAsync(byte slaveId, byte functionCode, byte[] pdu, int timeoutMs, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await EnsureConnectedAsync().ConfigureAwait(false);
        var tid = (ushort)Interlocked.Increment(ref _transactionId);
        var adu = BuildMbap(tid, slaveId, functionCode, pdu);
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock) { _pending[tid] = tcs; }

        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ct);
            using var registration = linkedCts.Token.Register(() =>
            {
                lock (_lock) { if (_pending.TryGetValue(tid, out var p) && p == tcs) _pending.Remove(tid); }
                tcs.TrySetException(new OperationCanceledException(ct.IsCancellationRequested ? "Operation cancelled" : $"MODBUS TCP timeout after {timeoutMs}ms"));
            }, useSynchronizationContext: false);

            await _stream!.WriteAsync(adu, linkedCts.Token).ConfigureAwait(false);

            return await tcs.Task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            lock (_lock) _pending.Remove(tid);
            throw;
        }
    }

    private async Task EnsureConnectedAsync()
    {
        if (_client.Connected) return;
        await _connectLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_client.Connected) return;
            var delay = 1_000;
            const int maxDelay = 30_000;
            const int maxRetries = 5;
            for (var attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    _client.Dispose();
                    _client = new TcpClient();
                    await _client.ConnectAsync(_host, _port).ConfigureAwait(false);
                    _stream = _client.GetStream();
                    _ = ReceiveLoopAsync(_receiveCts.Token);
                    return;
                }
                catch
                {
                    if (attempt == maxRetries - 1) throw;
                    await Task.Delay(delay).ConfigureAwait(false);
                    delay = Math.Min(delay * 2, maxDelay);
                }
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task HeartbeatAsync()
    {
        if (!_client.Connected) return;
        try
        {
            var adu = BuildMbap(0, 0x01, 0x07, []);
            await _stream!.WriteAsync(adu).ConfigureAwait(false);
        }
        catch
        {
            // silently ignore heartbeat failures
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var headerBuf = new byte[6];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var offset = 0;
                while (offset < 6)
                {
                    var read = await _stream!.ReadAsync(headerBuf.AsMemory(offset, 6 - offset), ct).ConfigureAwait(false);
                    if (read == 0) return;
                    offset += read;
                }

                var tid = (ushort)((headerBuf[0] << 8) | headerBuf[1]);
                var remaining = (headerBuf[4] << 8) | headerBuf[5];

                if (remaining < 2) continue;
                var body = new byte[remaining];
                offset = 0;
                while (offset < remaining)
                {
                    var read = await _stream!.ReadAsync(body.AsMemory(offset, remaining - offset), ct).ConfigureAwait(false);
                    if (read == 0) return;
                    offset += read;
                }

                TaskCompletionSource<byte[]>? tcs;
                lock (_lock)
                {
                    if (!_pending.TryGetValue(tid, out var p)) continue;
                    _pending.Remove(tid);
                    tcs = p;
                }

                var funcByte = body[1];
                if ((funcByte & 0x80) != 0)
                {
                    var excCode = body.Length > 2 ? body[2] : (byte)0;
                    tcs.TrySetException(new InvalidOperationException($"MODBUS exception 0x{excCode:X2}"));
                }
                else
                {
                    tcs.TrySetResult(body);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            lock (_lock)
            {
                foreach (var kv in _pending)
                    kv.Value.TrySetException(ex);
                _pending.Clear();
            }
        }
    }

    private static byte[] BuildMbap(ushort transactionId, byte slaveId, byte functionCode, byte[] pdu)
    {
        var adu = new byte[7 + pdu.Length];
        adu[0] = (byte)(transactionId >> 8);
        adu[1] = (byte)transactionId;
        adu[2] = 0; // protocol ID high
        adu[3] = 0; // protocol ID low
        adu[4] = (byte)((pdu.Length + 2) >> 8); // length high
        adu[5] = (byte)(pdu.Length + 2);         // length low
        adu[6] = slaveId;
        adu[7] = functionCode;
        Array.Copy(pdu, 0, adu, 8, pdu.Length);
        return adu;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _heartbeatTimer.Stop();
        _heartbeatTimer.Dispose();
        _receiveCts.Cancel();
        _receiveCts.Dispose();
        _stream?.Dispose();
        _client.Dispose();
        _connectLock.Dispose();
        lock (_lock)
        {
            foreach (var kv in _pending)
                kv.Value.TrySetException(new ObjectDisposedException(nameof(ModbusTcpTransport)));
            _pending.Clear();
        }
    }
}
