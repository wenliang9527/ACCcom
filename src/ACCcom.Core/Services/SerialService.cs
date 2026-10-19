using System.Buffers;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading.Channels;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class SerialService : ISerialService, IDisposable
{
    private SerialPort? _port;
    private int _rxEntryId;
    private int _txEntryId;
    private ReconnectSettings _reconnectSettings = new();
    private int _reconnectAttempt;
    private CancellationTokenSource? _reconnectCts;
    private SerialConfig? _lastConfig;
    private bool _waitingForDevice;
    private bool _disposed;
    private readonly MetricsCollector _metrics = MetricsCollector.Instance;

    private const int RxQueueCapacity = 10_000;

    /// <summary>RX dispatch queue. Reading bytes must finish on the
    /// SerialPort.DataReceived thread as fast as possible (the driver buffer is
    /// finite — fall behind and bytes are lost), but the subscriber chain — HTTP
    /// broadcast, trigger evaluation, disk logging, statistics — must not run on
    /// that thread: any one of them slowing down eats the driver buffer and
    /// drops data. Bounded + DropOldest keeps TryWrite non-blocking, so the
    /// receive loop can never be back-pressured by the drain side; a full queue
    /// means consumers cannot keep up, which is counted rather than swallowed.
    /// SingleReader keeps arrival order.</summary>
    private readonly Channel<LogEntry> _rxDispatch =
        Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(RxQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    private readonly CancellationTokenSource _drainCts = new();
    private Task? _drainTask;
    private int _rxDropped;

    public SerialService()
    {
        _drainTask = Task.Run(DrainRxAsync);
    }

    public bool IsOpen => _port?.IsOpen ?? false;
    public string? CurrentPort => _port?.PortName;
    public int BaudRate => _port?.BaudRate ?? 0;
    public SerialConfig? ActiveConfig => _port?.IsOpen == true ? _lastConfig : null;

    public event Action<LogEntry>? OnDataReceived;
    public event Action<string>? OnError;
    public event Action? OnDisconnected;
    /// <summary>Raised once when auto-reconnect pauses because the port is not present on the system.</summary>
    public event Action<string>? OnDeviceWait;

    public static string[] GetAvailablePorts() => SerialPort.GetPortNames();

    /// <summary>True when the port name is currently present on the system.</summary>
    private static bool PortExists(string portName)
    {
        try
        {
            return Array.Exists(SerialPort.GetPortNames(),
                p => string.Equals(p, portName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return true; // cannot tell — let the open attempt report the failure
        }
    }

    private const int MaxOpenRetries = 2;
    private const int OpenRetryDelayMs = 500;

    public bool Open(SerialConfig? config)
    {
        if (_port?.IsOpen == true) return true;
        // A null config would NRE below (outside the try); reject it up front.
        if (config == null) return false;

        for (int attempt = 0; attempt <= MaxOpenRetries; attempt++)
        {
            if (attempt > 0)
                Thread.Sleep(OpenRetryDelayMs);

            if (TryOpenOnce(config, out var error))
                return true;

            if (attempt == MaxOpenRetries)
                OnError?.Invoke($"[SerialService] Open failed after {MaxOpenRetries + 1} attempts: {error}");
        }

        return false;
    }

    /// <summary>Async open: the same retry policy as <see cref="Open"/>, but
    /// waiting between attempts with Task.Delay so a failing open (up to ~1s
    /// of retry sleeps) does not pin a thread-pool thread.</summary>
    public async Task<bool> OpenAsync(SerialConfig config)
    {
        if (_port?.IsOpen == true) return true;
        // VirtualSerialService-style guards fail cleanly on null; match Open.
        if (config == null) return false;

        for (int attempt = 0; attempt <= MaxOpenRetries; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(OpenRetryDelayMs).ConfigureAwait(false);

            if (TryOpenOnce(config, out var error))
                return true;

            if (attempt == MaxOpenRetries)
                OnError?.Invoke($"[SerialService] Open failed after {MaxOpenRetries + 1} attempts: {error}");
        }

        return false;
    }

    /// <summary>Single open attempt: constructs the port, opens it and records
    /// the active config; on failure the port is disposed and the error text is
    /// handed to the caller (retry policy lives in Open/OpenAsync).</summary>
    private bool TryOpenOnce(SerialConfig config, out string error)
    {
        error = "";
        try
        {
            // Construct inside the try: a bad config (invalid baud rate,
            // data bits, etc.) surfaces as a failed open, not an exception
            // that propagates past the retry loop.
            _port = new SerialPort(config.PortName, config.BaudRate, (Parity)config.Parity, config.DataBits, (StopBits)config.StopBits)
            {
                // UTF-8 so text-mode writes put on the wire exactly the bytes
                // the TX row displays and counts (Send computes RawHex via
                // Encoding.UTF8). SerialPort defaults to ASCII, which would
                // turn every non-ASCII char into '?' — display != wire.
                Encoding = System.Text.Encoding.UTF8,
                DtrEnable = config.DtrEnable,
                RtsEnable = config.RtsEnable,
                ReadTimeout = 1000,
                WriteTimeout = 1000
            };

            _port.DataReceived += OnSerialDataReceived;
            _port.ErrorReceived += OnSerialError;

            _port.Open();
            PreserveParityBytes(_port);
            _lastConfig = config;
            _reconnectSettings = config.Reconnect ?? new ReconnectSettings();
            _reconnectAttempt = 0;
            _metrics.RecordPortOpened();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            if (_port != null)
            {
                _port.DataReceived -= OnSerialDataReceived;
                _port.ErrorReceived -= OnSerialError;
                _port.Dispose();
                _port = null;
            }
            return false;
        }
    }

    public bool Close()
    {
        if (_disposed) return true;
        _reconnectCts?.Cancel();
        // Entries already read from the port must still reach the buffers and
        // disk — a close that dropped the tail of the stream would silently
        // truncate the session log.
        DrainPendingAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        ClosePortOnly();
        return true;
    }

    /// <summary>Waits (bounded) for the dispatch queue to drain. Does not
    /// complete the channel: SerialService instances are reopened across
    /// sessions, so the queue must outlive an individual Close.</summary>
    internal async Task DrainPendingAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (_rxDispatch.Reader.Count > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(5).ConfigureAwait(false);
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
                    // One misbehaving subscriber (e.g. a Modbus parser throwing
                    // on malformed frames) must not kill the drain loop.
                    OnError?.Invoke($"[SerialService] Subscriber error: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            OnError?.Invoke($"[SerialService] RX drain stopped: {ex.Message}");
        }
    }

    /// <summary>Hands an RX entry to the drain task. The subscriber chain must
    /// not run on the SerialPort.DataReceived thread; DropOldest makes TryWrite
    /// always succeed — a full queue evicts the oldest entry instead of
    /// failing — so the receive loop is never blocked either way. The depth
    /// read before the write tells whether that write caused an eviction (the
    /// queue was already full); the receive thread is the only writer in
    /// practice, which makes this exact.</summary>
    private void DispatchRx(LogEntry entry)
    {
        var wasFull = _rxDispatch.Reader.Count >= RxQueueCapacity;
        _rxDispatch.Writer.TryWrite(entry);

        if (!wasFull) return;

        var dropped = Interlocked.Increment(ref _rxDropped);
        if (dropped % 1000 == 1)
            OnError?.Invoke($"[SerialService] RX dispatch queue overflow; {dropped} entries dropped");
    }

    /// <summary>Test seam: inject an entry through the same dispatch path the
    /// receive thread uses, so drop accounting is exercised too.</summary>
    internal void DispatchForTest(LogEntry entry) => DispatchRx(entry);

    private void ClosePortOnly()
    {
        if (_port == null) return;
        try
        {
            if (_port.IsOpen)
            {
                _port.DataReceived -= OnSerialDataReceived;
                _port.ErrorReceived -= OnSerialError;
                _port.Close();
                _metrics.RecordPortClosed();
            }
            _port.Dispose();
            _port = null;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"[SerialService] Close failed: {ex.Message}");
            _port?.Dispose();
            _port = null;
        }
    }

    public bool Send(string? data, bool isHex = false)
    {
        // Reject null/empty up front: the hex/utf8 conversion would throw
        // inside the retry loop and burn a full 500ms retry delay before
        // reporting a misleading failure.
        if (string.IsNullOrEmpty(data))
            return false;

        // Parse once before retrying: malformed input cannot be repaired by a retry.
        byte[] hexBytes = Array.Empty<byte>();
        if (isHex && !HexHelper.TryHexStringToBytes(data, out hexBytes))
        {
            OnError?.Invoke($"[SerialService] Send failed: invalid hex '{data}'");
            return false;
        }

        if (_port?.IsOpen != true)
        {
            OnError?.Invoke("[SerialService] Send failed: serial port not open");
            return false;
        }

        const int maxRetries = 1;
        const int retryDelayMs = 500;
        Exception? lastException = null;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (attempt > 0)
                Thread.Sleep(retryDelayMs);

            try
            {
                long sentBytes;
                string hexStr;
                if (isHex)
                {
                    hexStr = HexHelper.BytesToHexSpaced(hexBytes, 0, hexBytes.Length);
                    _port.Write(hexBytes, 0, hexBytes.Length);
                    sentBytes = hexBytes.Length;
                }
                else
                {
                    var textBytes = System.Text.Encoding.UTF8.GetBytes(data);
                    _port.Write(data);
                    sentBytes = textBytes.Length;
                    hexStr = HexHelper.BytesToHexSpaced(textBytes, 0, textBytes.Length);
                }
                _metrics.RecordBytesSent(sentBytes);

                var entry = new LogEntry
                {
                    Id = Interlocked.Increment(ref _txEntryId),
                    Timestamp = DateTime.Now,
                    Direction = "TX",
                    RawHex = hexStr,
                    Text = data
                };
                OnDataReceived?.Invoke(entry);
                return true;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        OnError?.Invoke($"[SerialService] Send failed after {maxRetries + 1} attempts: {lastException?.Message}");
        return false;
    }

    public bool SendHex(string hex)
    {
        return Send(hex, true);
    }

    /// <summary>
    /// Keeps the raw on-the-wire byte on parity-framed links. With parity
    /// enabled, the Windows serial driver replaces any byte that fails the
    /// parity check with <see cref="SerialPort.ParityReplace"/> (default
    /// '?' = 0x3F) before <see cref="SerialPort.Read(byte[], int, int)"/>
    /// hands it to us, so the hex/text view would show 3F instead of the byte
    /// actually received — the original byte is already lost by then, so this
    /// cannot be fixed in the display layer. Setting ParityReplace to 0 (null)
    /// disables that substitution; the parity fault is still reported via
    /// <see cref="SerialPort.ErrorReceived"/> (SerialError.RXParity). Applied
    /// post-open so it reaches the live DCB, and guarded so a setter failure
    /// never discards an otherwise-good open.
    /// </summary>
    private static void PreserveParityBytes(SerialPort port)
    {
        if (port.Parity == Parity.None) return;
        try
        {
            port.ParityReplace = 0;
        }
        catch (Exception ex)
        {
            // Non-fatal fallback: worst case the driver keeps its default '?'.
            Debug.WriteLine($"[SerialService] ParityReplace set failed: {ex.Message}");
        }
    }

    private void OnSerialDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_port?.IsOpen != true) return;

        try
        {
            int bytesToRead = _port.BytesToRead;
            if (bytesToRead <= 0) return;

            var buffer = ArrayPool<byte>.Shared.Rent(bytesToRead);
            try
            {
                int bytesRead = _port.Read(buffer, 0, bytesToRead);
                _metrics.RecordBytesReceived(bytesRead);
                var hex = HexHelper.BytesToHexSpaced(buffer, 0, bytesRead);
                var text = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);

                var entry = new LogEntry
                {
                    Id = Interlocked.Increment(ref _rxEntryId),
                    Timestamp = DateTime.Now,
                    Direction = "RX",
                    RawHex = hex,
                    Text = text,
                    // The producer knows the payload size at read time; carrying
                    // it saves every consumer a rescan of the hex string.
                    ByteCount = bytesRead
                };
                DispatchRx(entry);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"[SerialService] Receive failed: {ex.Message}");
        }
    }

    private void OnSerialError(object sender, SerialErrorReceivedEventArgs e)
    {
        _metrics.RecordError();
        OnError?.Invoke($"[SerialService] Port error: {e.EventType}");
        if (_port?.IsOpen != true)
        {
            OnDisconnected?.Invoke();
            _ = Task.Run(async () =>
            {
                try { await StartAutoReconnectAsync().ConfigureAwait(false); }
                catch (Exception ex) { OnError?.Invoke($"[SerialService] Auto reconnect failed: {ex.Message}"); }
            });
        }
    }

    private async Task StartAutoReconnectAsync()
    {
        if (!_reconnectSettings.AutoReconnect || _lastConfig == null) return;
        var oldCts = _reconnectCts;
        _reconnectCts = new CancellationTokenSource();
        oldCts?.Cancel();
        var token = _reconnectCts.Token;

        try
        {
            // maxAttempts == 0 means unlimited
            while ((_reconnectSettings.MaxReconnectAttempts == 0 || _reconnectAttempt < _reconnectSettings.MaxReconnectAttempts)
                   && !token.IsCancellationRequested)
            {
                // Apply backoff: delay = interval * (backoff ^ attempt)
                var delay = ComputeReconnectDelayMs(
                    _reconnectSettings.ReconnectIntervalMs,
                    _reconnectSettings.BackoffMultiplier,
                    _reconnectAttempt);
                await Task.Delay(delay, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) break;
                if (_port?.IsOpen == true) break;

                // 断联复检: when the device is unplugged the port disappears from
                // the system. Wait for it to reappear instead of burning
                // reconnect attempts on a port that cannot open.
                if (!PortExists(_lastConfig.PortName))
                {
                    if (!_waitingForDevice)
                    {
                        _waitingForDevice = true;
                        OnDeviceWait?.Invoke(_lastConfig.PortName);
                    }
                    continue;
                }
                _waitingForDevice = false;

                _reconnectAttempt++;
                try
                {
                    var tempPort = new SerialPort(_lastConfig.PortName, _lastConfig.BaudRate, (Parity)_lastConfig.Parity, _lastConfig.DataBits, (StopBits)_lastConfig.StopBits)
                    {
                        Encoding = System.Text.Encoding.UTF8,
                        DtrEnable = _lastConfig.DtrEnable,
                        RtsEnable = _lastConfig.RtsEnable,
                        ReadTimeout = 1000,
                        WriteTimeout = 1000
                    };
                    tempPort.Open();
                    PreserveParityBytes(tempPort);
                    ClosePortOnly();
                    _port = tempPort;
                    _port.DataReceived += OnSerialDataReceived;
                    _port.ErrorReceived += OnSerialError;
                    var msg = $"[Auto reconnect] Succeeded on attempt #{_reconnectAttempt}";
                    DispatchRx(new LogEntry
                    {
                        Id = Interlocked.Increment(ref _rxEntryId),
                        Timestamp = DateTime.Now,
                        Direction = "RX",
                        RawHex = "",
                        Text = msg
                    });
                    return;
                }
                catch
                {
                    if (_reconnectSettings.MaxReconnectAttempts > 0
                        && _reconnectAttempt >= _reconnectSettings.MaxReconnectAttempts)
                    {
                        OnError?.Invoke($"[SerialService] Auto reconnect failed after {_reconnectSettings.MaxReconnectAttempts} attempts");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnError?.Invoke($"[SerialService] Auto reconnect error: {ex.Message}");
        }
    }

    /// <summary>
    /// Exponential-backoff delay for a reconnect attempt: interval * backoff^attempt.
    /// Clamped to a non-negative value so runaway backoff cannot overflow or delay forever.
    /// </summary>
    internal static int ComputeReconnectDelayMs(int intervalMs, double backoffMultiplier, int attempt)
    {
        if (intervalMs <= 0) return 0;
        if (backoffMultiplier <= 0) return intervalMs;
        if (attempt < 0) attempt = 0;
        try
        {
            var delay = intervalMs * Math.Pow(backoffMultiplier, attempt);
            if (delay > int.MaxValue) return int.MaxValue;
            return (int)delay;
        }
        catch
        {
            return intervalMs;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _reconnectCts?.Cancel();
            _reconnectCts?.Dispose();
            _reconnectCts = null;
            // Close first (it drains entries already read from the port), then
            // stop the drain loop — cancelling first would discard that tail.
            Close();
            _drainCts.Cancel();
            _drainCts.Dispose();
        }
        _disposed = true;
    }
}
