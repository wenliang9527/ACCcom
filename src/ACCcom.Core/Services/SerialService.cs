using System.Buffers;
using System.Diagnostics;
using System.IO.Ports;
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

    public bool IsOpen => _port?.IsOpen ?? false;
    public string? CurrentPort => _port?.PortName;
    public int BaudRate => _port?.BaudRate ?? 0;

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

    public bool Open(SerialConfig config)
    {
        if (_port?.IsOpen == true) return true;

        const int maxRetries = 2;
        const int retryDelayMs = 500;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (attempt > 0)
                Thread.Sleep(retryDelayMs);

            _port = new SerialPort(config.PortName, config.BaudRate, (Parity)config.Parity, config.DataBits, (StopBits)config.StopBits)
            {
                DtrEnable = config.DtrEnable,
                RtsEnable = config.RtsEnable,
                ReadTimeout = 1000,
                WriteTimeout = 1000
            };

            _port.DataReceived += OnSerialDataReceived;
            _port.ErrorReceived += OnSerialError;

            try
            {
                _port.Open();
                _lastConfig = config;
                _reconnectSettings = config.Reconnect ?? new ReconnectSettings();
                _reconnectAttempt = 0;
                _metrics.RecordPortOpened();
                return true;
            }
            catch (Exception ex)
            {
                _port.DataReceived -= OnSerialDataReceived;
                _port.ErrorReceived -= OnSerialError;
                _port?.Dispose();
                _port = null;

                if (attempt == maxRetries)
                {
                    OnError?.Invoke($"[SerialService] Open failed after {maxRetries + 1} attempts: {ex.Message}");
                    return false;
                }
            }
        }

        return false;
    }

    public bool Close()
    {
        if (_disposed) return true;
        _reconnectCts?.Cancel();
        ClosePortOnly();
        return true;
    }

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

    public bool Send(string data, bool isHex = false)
    {
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
                    hexStr = data.Replace(" ", "");
                    var bytes = Convert.FromHexString(hexStr);
                    _port.Write(bytes, 0, bytes.Length);
                    sentBytes = bytes.Length;
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
                    Text = text
                };
                OnDataReceived?.Invoke(entry);
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
                var delay = (int)(_reconnectSettings.ReconnectIntervalMs
                    * Math.Pow(_reconnectSettings.BackoffMultiplier, _reconnectAttempt));
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
                        DtrEnable = _lastConfig.DtrEnable,
                        RtsEnable = _lastConfig.RtsEnable,
                        ReadTimeout = 1000,
                        WriteTimeout = 1000
                    };
                    tempPort.Open();
                    ClosePortOnly();
                    _port = tempPort;
                    _port.DataReceived += OnSerialDataReceived;
                    _port.ErrorReceived += OnSerialError;
                    var msg = $"[Auto reconnect] Succeeded on attempt #{_reconnectAttempt}";
                    OnDataReceived?.Invoke(new LogEntry
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
            Close();
        }
        _disposed = true;
    }
}
