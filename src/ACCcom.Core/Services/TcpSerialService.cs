using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

/// <summary>
/// ISerialService over a raw TCP connection, for serial-over-TCP bridges
/// (the windowsair wireless-DAP UART bridge listening on TCP 1234 being the
/// reference target). The TCP side carries bytes verbatim; the UART side of
/// the bridge runs at a baud rate the bridge itself controls.
///
/// Connect handshake: the bridge treats the FIRST packet after connect as a
/// baud command when it is 2-7 plain ASCII digits (1..1999999), consumes it,
/// and switches its UART to that rate — and the window closes on the first
/// packet no matter what it contains. So Open always sends the configured
/// baudRate as that first packet (digits only, no terminator, before Open
/// returns, so no caller send can precede it): it sets the bridge-side rate
/// AND deterministically consumes the window, after which every payload —
/// short digit strings included — is forwarded verbatim. The handshake goes
/// through the normal TX path, so it shows up in buffers/traffic_log as an
/// audited TX row.
///
/// SetBaudRate re-establishes the connection and re-runs the handshake with
/// the new rate (the command window only exists right after connect), rather
/// than shipping a mid-stream digits packet that the bridge would forward as
/// garbage. The reconnect is transparent to the session: buffer, tag and
/// config survive; the gap costs in-flight bytes (the bridge drops UART RX
/// while no client is attached).
///
/// Socket-lifetime guarantee (the bridge is single-client — a zombie
/// connection deadlocks the whole UART path): every in-process abandon path
/// (close, dispose, reconnect swap, error teardown) funnels through
/// TeardownCurrent; a host shutdown funnels through Dispose (the MCP host
/// disposes its singletons); a hard process crash is covered by the OS,
/// which closes all handles at termination (FIN, or RST when receive data
/// is pending) — the bridge sees the connection die and frees its client
/// slot. The finalizer below is the last net for an instance that is simply
/// leaked without Dispose, mirroring SerialPort's Component finalizer.
/// </summary>
public class TcpSerialService : ISerialService, IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private volatile bool _connValid;   // own flag: TcpClient.Connected reflects only the last IO op
    private volatile bool _tearingDown; // Close/SetBaud transition: read loop must exit quietly
    private TcpEndpoint _endpoint;
    private SerialConfig? _lastConfig;
    private ReconnectSettings _reconnectSettings = new();
    private int _reconnectAttempt;
    private CancellationTokenSource? _reconnectCts;
    private CancellationTokenSource? _readCts;
    /// <summary>Serializes every connection lifecycle transition (open,
    /// close, reconnect attempt, SetBaud swap). Async-capable because these
    /// paths await TCP connects; a plain lock cannot span them.</summary>
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private bool _disposed;
    private readonly MetricsCollector _metrics = MetricsCollector.Instance;
    private int _rxEntryId;
    private int _txEntryId;

    private const int ConnectTimeoutMs = 3_000;
    private const int ReadChunkBytes = 4096;
    private const int MaxOpenRetries = 2;
    private const int OpenRetryDelayMs = 500;

    /// <summary>Serializes stream writes. Same rationale as SerialService's
    /// gate: two real senders can race on one service and interleaved
    /// NetworkStream writes corrupt frames on the wire. Retry waits stay
    /// outside the gate.</summary>
    private readonly object _sendGate = new();

    /// <summary>RX dispatch queue — same design as SerialService: the read
    /// loop must never run the subscriber chain (HTTP broadcast, triggers,
    /// disk logging) inline, and must never be back-pressured by it.</summary>
    private const int RxQueueCapacity = 10_000;
    private readonly Channel<LogEntry> _rxDispatch =
        Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(RxQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    private readonly CancellationTokenSource _drainCts = new();
    private readonly Task _drainTask;
    private int _rxDropped;

    public TcpSerialService()
    {
        _drainTask = Task.Run(DrainRxAsync);
    }

    public bool IsOpen => _connValid;
    public string? CurrentPort => _connValid ? _endpoint.ToString() : null;
    public int BaudRate => _connValid ? _lastConfig?.BaudRate ?? 0 : 0;
    public SerialConfig? ActiveConfig => _connValid ? _lastConfig : null;

    public event Action<LogEntry>? OnDataReceived;
    public event Action<string>? OnError;
    public event Action? OnDisconnected;
#pragma warning disable CS0067 // never raised: TCP has no "device unplugged" pause state — a dead endpoint just refuses connects
    public event Action<string>? OnDeviceWait;
#pragma warning restore CS0067

    public bool Open(SerialConfig? config)
    {
        // Sync callers (GUI paths) are background threads; blocking over the
        // async core (all ConfigureAwait(false)) is safe and avoids a second
        // implementation of the retry policy.
        return OpenAsync(config).GetAwaiter().GetResult();
    }

    public async Task<bool> OpenAsync(SerialConfig? config)
    {
        if (_connValid) return true;
        if (config == null) return false;
        if (!TcpEndpoint.TryParse(config.PortName, out _endpoint))
        {
            OnError?.Invoke("[TcpSerialService] Open failed: port name is not a TCP endpoint (use tcp://host:port)");
            return false;
        }

        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            for (int attempt = 0; attempt <= MaxOpenRetries; attempt++)
            {
                if (attempt > 0)
                    await Task.Delay(OpenRetryDelayMs).ConfigureAwait(false);
                if (_connValid) return true; // a reconnect loop won while we waited on the gate

                if (await TryConnectOnceAsync(config).ConfigureAwait(false))
                    return true;
            }
            OnError?.Invoke($"[TcpSerialService] Open failed after {MaxOpenRetries + 1} attempts: connect {_endpoint} refused or timed out");
            return false;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>One connect attempt: dial, start the read loop, send the
    /// baud handshake. Called with _lifecycle held. No error events — the
    /// callers (explicit open, reconnect loop) own error reporting.</summary>
    private async Task<bool> TryConnectOnceAsync(SerialConfig config)
    {
        TcpClient? client = null;
        try
        {
            client = new TcpClient();
            using (var timeoutCts = new CancellationTokenSource(ConnectTimeoutMs))
                await client.ConnectAsync(_endpoint.Host, _endpoint.Port, timeoutCts.Token).ConfigureAwait(false);

            var stream = client.GetStream();
            _client = client;
            _stream = stream;
            _connValid = true;
            _tearingDown = false;
            _lastConfig = config;
            _reconnectSettings = config.Reconnect ?? new ReconnectSettings();
            _reconnectAttempt = 0;
            _readCts = new CancellationTokenSource();
            _ = Task.Run(() => ReadLoopAsync(stream, _readCts.Token));
            _metrics.RecordPortOpened();

            // Handshake under the send gate so an early concurrent Send can
            // never slip ahead of it (TCP preserves write order).
            var handshake = Encoding.ASCII.GetBytes(config.BaudRate.ToString());
            lock (_sendGate)
            {
                stream.Write(handshake, 0, handshake.Length);
                stream.Flush();
            }
            RecordTx(handshake, config.BaudRate.ToString());
            return true;
        }
        catch (Exception)
        {
            if (client != null)
            {
                try { client.Close(); } catch { /* already dead */ }
            }
            if (ReferenceEquals(_client, client))
            {
                // The half-adopted connection is ours to roll back.
                _client = null;
                _stream = null;
                _connValid = false;
            }
            return false;
        }
    }

    private async Task ReadLoopAsync(NetworkStream stream, CancellationToken token)
    {
        var buffer = new byte[ReadChunkBytes];
        try
        {
            while (!token.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                if (read == 0) break; // orderly shutdown by the peer
                _metrics.RecordBytesReceived(read);
                DispatchRx(new LogEntry
                {
                    Id = Interlocked.Increment(ref _rxEntryId),
                    Timestamp = DateTime.Now,
                    Direction = "RX",
                    RawHex = HexHelper.BytesToHexSpaced(buffer, 0, read),
                    Text = Encoding.UTF8.GetString(buffer, 0, read),
                    ByteCount = read
                });
            }
        }
        catch (OperationCanceledException) { /* local teardown */ }
        catch (Exception) { /* socket death — handled below */ }

        if (_tearingDown || _disposed) return;       // expected teardown: owner reports state
        await HandleUnexpectedDisconnectAsync().ConfigureAwait(false);
    }

    /// <summary>Read-loop death that nobody asked for (peer drop, WiFi
    /// blip). Teardown + OnDisconnected + hand off to the auto-reconnect
    /// loop, which re-runs the baud handshake on every reconnect (a
    /// mid-session firmware reboot resets the UART to its compile-time
    /// default; the idempotent re-assert heals that).</summary>
    private async Task HandleUnexpectedDisconnectAsync()
    {
        if (!await _lifecycle.WaitAsync(0).ConfigureAwait(false)) return; // a Close/SetBaud/open owns the transition
        try
        {
            if (_tearingDown || !_connValid || _disposed) return;
            TeardownCurrent();
            OnDisconnected?.Invoke();
            if (_reconnectSettings.AutoReconnect && !_disposed)
                _ = Task.Run(async () =>
                {
                    try { await AutoReconnectAsync().ConfigureAwait(false); }
                    catch (Exception ex) { OnError?.Invoke($"[TcpSerialService] Auto reconnect failed: {ex.Message}"); }
                });
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task AutoReconnectAsync()
    {
        if (!_reconnectSettings.AutoReconnect || _lastConfig == null) return;
        var oldCts = _reconnectCts;
        _reconnectCts = new CancellationTokenSource();
        oldCts?.Cancel();
        var token = _reconnectCts.Token;

        try
        {
            while ((_reconnectSettings.MaxReconnectAttempts == 0 || _reconnectAttempt < _reconnectSettings.MaxReconnectAttempts)
                   && !token.IsCancellationRequested)
            {
                var delay = SerialService.ComputeReconnectDelayMs(
                    _reconnectSettings.ReconnectIntervalMs,
                    _reconnectSettings.BackoffMultiplier,
                    _reconnectAttempt);
                await Task.Delay(delay, token).ConfigureAwait(false);
                if (token.IsCancellationRequested || _connValid || _disposed) break;

                _reconnectAttempt++;
                await _lifecycle.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (_connValid || _disposed || token.IsCancellationRequested) break;
                    // _lastConfig (kept in sync by SetBaudRate) carries the
                    // CURRENT baud, so the reconnect handshake re-asserts it.
                    if (await TryConnectOnceAsync(_lastConfig).ConfigureAwait(false))
                    {
                        DispatchRx(new LogEntry
                        {
                            Id = Interlocked.Increment(ref _rxEntryId),
                            Timestamp = DateTime.Now,
                            Direction = "RX",
                            RawHex = "",
                            Text = $"[Auto reconnect] Succeeded on attempt #{_reconnectAttempt}"
                        });
                        return;
                    }
                }
                finally
                {
                    _lifecycle.Release();
                }
            }
        }
        catch (OperationCanceledException) { /* closed while waiting */ }
        catch (Exception ex)
        {
            OnError?.Invoke($"[TcpSerialService] Auto reconnect error: {ex.Message}");
        }
    }

    public bool Send(string? data, bool isHex = false)
    {
        if (!TryPrepareSend(data, isHex, out var hexBytes, out var text, out var error))
        {
            if (error != null) OnError?.Invoke(error);
            return false;
        }

        const int maxRetries = 1;
        const int retryDelayMs = 500;
        Exception? lastException = null;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (attempt > 0)
                Thread.Sleep(retryDelayMs);
            lock (_sendGate)
            {
                if (TrySendPrepared(hexBytes, text, out lastException)) return true;
            }
        }
        OnError?.Invoke($"[TcpSerialService] Send failed after {maxRetries + 1} attempts: {lastException?.Message}");
        return false;
    }

    public async Task<bool> SendAsync(string? data, bool isHex = false)
    {
        if (!TryPrepareSend(data, isHex, out var hexBytes, out var text, out var error))
        {
            if (error != null) OnError?.Invoke(error);
            return false;
        }

        const int maxRetries = 1;
        const int retryDelayMs = 500;
        Exception? lastException = null;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(retryDelayMs).ConfigureAwait(false);
            lock (_sendGate)
            {
                if (TrySendPrepared(hexBytes, text, out lastException)) return true;
            }
        }
        OnError?.Invoke($"[TcpSerialService] Send failed after {maxRetries + 1} attempts: {lastException?.Message}");
        return false;
    }

    /// <summary>Shared up-front validation, mirroring SerialService: empty
    /// input is a silent rejection, malformed hex raises once, a closed
    /// connection raises once — none of these are repaired by a retry.</summary>
    private bool TryPrepareSend(string? data, bool isHex, out byte[]? hexBytes, out string? text, out string? error)
    {
        hexBytes = null;
        text = data;
        error = null;

        if (string.IsNullOrEmpty(data))
            return false;

        if (isHex)
        {
            if (!HexHelper.TryHexStringToBytes(data, out var parsed))
            {
                error = $"[TcpSerialService] Send failed: invalid hex '{data}'";
                return false;
            }
            hexBytes = parsed;
        }

        if (!_connValid || _stream == null)
        {
            error = "[TcpSerialService] Send failed: TCP connection not open";
            return false;
        }

        return true;
    }

    /// <summary>One write attempt. Text mode writes the UTF-8 bytes (same
    /// wire==display contract as SerialService); hex mode writes the parsed
    /// bytes. TX entries fire on the caller thread, like SerialService.</summary>
    private bool TrySendPrepared(byte[]? hexBytes, string? text, out Exception? error)
    {
        error = null;
        var stream = _stream;
        if (stream == null || !_connValid)
        {
            error = new InvalidOperationException("connection lost");
            return false;
        }
        try
        {
            var payload = hexBytes ?? Encoding.UTF8.GetBytes(text!);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
            _metrics.RecordBytesSent(payload.Length);
            RecordTx(payload, text ?? HexHelper.BytesToHexSpaced(payload, 0, payload.Length));
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    private void RecordTx(byte[] payload, string displayText)
    {
        OnDataReceived?.Invoke(new LogEntry
        {
            Id = Interlocked.Increment(ref _txEntryId),
            Timestamp = DateTime.Now,
            Direction = "TX",
            RawHex = HexHelper.BytesToHexSpaced(payload, 0, payload.Length),
            Text = displayText,
            ByteCount = payload.Length
        });
    }

    public bool SendHex(string hex) => Send(hex, true);

    /// <summary>Transparent rate change: the bridge only accepts a baud
    /// command as the first packet after connect, so this tears the
    /// connection down and re-establishes it with the new rate as the
    /// handshake. Buffer/tag/session survive; the momentary gap drops
    /// in-flight bytes (the bridge discards UART RX with no client). On an
    /// immediate reconnect failure the service stays down and the normal
    /// auto-reconnect loop keeps trying — at the NEW rate, via the synced
    /// last config.</summary>
    public bool SetBaudRate(int baudRate)
    {
        if (baudRate <= 0) return false;
        if (!_connValid || _lastConfig == null) return false;

        _lifecycle.Wait();
        try
        {
            if (!_connValid || _disposed) return false; // lost the race to a disconnect
            _tearingDown = true; // read loop exits quietly — this is not a "disconnect"
            TeardownCurrent();
            _lastConfig.BaudRate = baudRate;

            if (TryConnectOnceAsync(_lastConfig).GetAwaiter().GetResult())
            {
                DispatchRx(new LogEntry
                {
                    Id = Interlocked.Increment(ref _rxEntryId),
                    Timestamp = DateTime.Now,
                    Direction = "RX",
                    RawHex = "",
                    Text = $"[TCP] Baud switched to {baudRate} (reconnected)"
                });
                return true;
            }

            OnError?.Invoke($"[TcpSerialService] Set baud {baudRate}: reconnect to {_endpoint} failed; auto reconnect will keep trying at the new rate");
            if (_reconnectSettings.AutoReconnect && !_disposed)
                _ = Task.Run(() => AutoReconnectAsync());
            return false;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public bool Close()
    {
        if (_disposed) return true;
        _reconnectCts?.Cancel();
        _lifecycle.Wait();
        try
        {
            // Drain entries the read loop already dispatched before tearing
            // the connection — same tail-preservation contract as
            // SerialService.Close.
            DrainPendingAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            TeardownCurrent();
        }
        finally
        {
            _lifecycle.Release();
        }
        return true;
    }

    public async Task<bool> CloseAsync()
    {
        if (_disposed) return true;
        _reconnectCts?.Cancel();
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await DrainPendingAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            TeardownCurrent();
        }
        finally
        {
            _lifecycle.Release();
        }
        return true;
    }

    /// <summary>Waits (bounded) for the dispatch queue to drain. The channel
    /// itself outlives individual connections — the service is reopened
    /// across sessions.</summary>
    internal async Task DrainPendingAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (_rxDispatch.Reader.Count > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(5).ConfigureAwait(false);
    }

    private void TeardownCurrent()
    {
        _tearingDown = true;
        _connValid = false;
        _readCts?.Cancel();
        _readCts = null;
        try { _client?.Close(); } catch { /* already dead */ }
        _client = null;
        _stream = null;
        _metrics.RecordPortClosed();
    }

    private async Task DrainRxAsync()
    {
        try
        {
            await foreach (var entry in _rxDispatch.Reader.ReadAllAsync(_drainCts.Token).ConfigureAwait(false))
            {
                try
                {
                    OnDataReceived?.Invoke(entry);
                }
                catch (Exception ex)
                {
                    OnError?.Invoke($"[TcpSerialService] Subscriber error: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            OnError?.Invoke($"[TcpSerialService] RX drain stopped: {ex.Message}");
        }
    }

    private void DispatchRx(LogEntry entry)
    {
        var wasFull = _rxDispatch.Reader.Count >= RxQueueCapacity;
        _rxDispatch.Writer.TryWrite(entry);
        if (!wasFull) return;

        var dropped = Interlocked.Increment(ref _rxDropped);
        if (dropped % 1000 == 1)
            OnError?.Invoke($"[TcpSerialService] RX dispatch queue overflow; {dropped} entries dropped");
    }

    public void Dispose()
    {
        // _disposed is set LAST: Close() must not see it, or it would skip
        // the teardown this path relies on.
        if (_disposed) return;
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = null;
        Close();
        _drainCts.Cancel();
        _drainCts.Dispose();
        // _lifecycle is deliberately NOT disposed: a read loop that is dying
        // right now may still take its WaitAsync(0) probe, and disposing a
        // semaphore under that race throws into an unobserved task.
        _disposed = true;
    }

    /// <summary>Finalizer safety net for a leaked instance (no Dispose).
    /// Finalizer-safe work only: no events, no channel or semaphore traffic.
    /// _disposed goes first: the read-loop death this close triggers must see
    /// it and skip auto-reconnect, or a finalized instance would be
    /// resurrected by its own reconnect loop and re-dial the bridge.
    /// Honest limitation: an instance with a pending read is rooted by the
    /// in-flight socket operation and will not be finalized — this net only
    /// fires for instances that are unconnected or whose read has already
    /// completed. The residual (leaked CONNECTED instance) has no producer
    /// in the current composition and is backstopped by the planned
    /// firmware-side idle timeout.</summary>
    ~TcpSerialService()
    {
        try
        {
            _disposed = true;
            _readCts?.Cancel();
            _client?.Close();
        }
        catch { /* a finalizer must never throw */ }
    }
}
