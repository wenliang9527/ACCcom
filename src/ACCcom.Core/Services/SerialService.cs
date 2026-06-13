using System.Buffers;
using System.IO.Ports;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class SerialService : IDisposable
{
    private SerialPort? _port;
    private int _rxEntryId;
    private int _txEntryId;
    private ReconnectSettings _reconnectSettings = new();
    private int _reconnectAttempt;
    private CancellationTokenSource? _reconnectCts;
    private SerialConfig? _lastConfig;
    private bool _disposed;

    public bool IsOpen => _port?.IsOpen ?? false;
    public string? CurrentPort => _port?.PortName;
    public int BaudRate => _port?.BaudRate ?? 0;

    public event Action<LogEntry>? OnDataReceived;
    public event Action<string>? OnError;
    public event Action? OnDisconnected;

    public static string[] GetAvailablePorts() => SerialPort.GetPortNames();

    public bool Open(SerialConfig config)
    {
        if (_port?.IsOpen == true) return true;

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
            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Failed to open serial port: {ex.Message}");
            _port?.Dispose();
            _port = null;
            return false;
        }
    }

    public bool Close()
    {
        if (_disposed) return true;
        _reconnectCts?.Cancel();
        if (_port == null) return true;
        try
        {
            if (_port.IsOpen)
            {
                _port.DataReceived -= OnSerialDataReceived;
                _port.ErrorReceived -= OnSerialError;
                _port.Close();
            }
            _port.Dispose();
            _port = null;
            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Failed to close serial port: {ex.Message}");
            _port?.Dispose();
            _port = null;
            return false;
        }
    }

    public bool Send(string data, bool isHex = false)
    {
        if (_port?.IsOpen != true)
        {
            OnError?.Invoke("Serial port not open");
            return false;
        }

        try
        {
            if (isHex)
            {
                var bytes = Convert.FromHexString(data.Replace(" ", ""));
                _port.Write(bytes, 0, bytes.Length);
            }
            else
            {
                _port.Write(data);
            }

            var entry = new LogEntry
            {
                Id = Interlocked.Increment(ref _txEntryId),
                Timestamp = DateTime.Now,
                Direction = "TX",
                RawHex = isHex ? data.Replace(" ", "") : HexHelper.BytesToHexSpaced(System.Text.Encoding.UTF8.GetBytes(data), 0, System.Text.Encoding.UTF8.GetByteCount(data)),
                Text = data
            };
            OnDataReceived?.Invoke(entry);
            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Send failed: {ex.Message}");
            return false;
        }
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
            OnError?.Invoke($"Receive data error: {ex.Message}");
        }
    }

    private void OnSerialError(object sender, SerialErrorReceivedEventArgs e)
    {
        OnError?.Invoke($"Serial port error: {e.EventType}");
        if (_port?.IsOpen != true)
        {
            OnDisconnected?.Invoke();
            _ = StartAutoReconnectAsync();
        }
    }

    public void EnableAutoReconnect(bool enable, int maxAttempts = 10, int delayMs = 1000)
    {
        _reconnectSettings.AutoReconnect = enable;
        _reconnectSettings.MaxReconnectAttempts = maxAttempts;
        _reconnectSettings.ReconnectIntervalMs = delayMs;
        if (!enable)
        {
            _reconnectCts?.Cancel();
        }
    }

    public void UpdateReconnectSettings(ReconnectSettings settings)
    {
        _reconnectSettings = settings;
        if (!settings.AutoReconnect)
        {
            _reconnectCts?.Cancel();
        }
    }

    private async Task StartAutoReconnectAsync()
    {
        if (!_reconnectSettings.AutoReconnect || _lastConfig == null) return;
        _reconnectCts = new CancellationTokenSource();
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
                await Task.Delay(delay, token);
                if (token.IsCancellationRequested) break;
                if (_port?.IsOpen == true) break;

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
                    Close();
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
                        OnError?.Invoke($"Auto reconnect failed after {_reconnectSettings.MaxReconnectAttempts} attempts");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnError?.Invoke($"Auto reconnect error: {ex.Message}");
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
