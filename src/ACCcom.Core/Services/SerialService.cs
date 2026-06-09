using System.IO.Ports;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class SerialService : IDisposable
{
    private SerialPort? _port;
    private int _entryId;
    private bool _autoReconnect = true;
    private int _reconnectMaxAttempts = 10;
    private int _reconnectAttempt;
    private int _reconnectDelayMs = 1000;
    private CancellationTokenSource? _reconnectCts;
    private SerialConfig? _lastConfig;

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
            _reconnectAttempt = 0;
            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"打开串口失败: {ex.Message}");
            _port?.Dispose();
            _port = null;
            return false;
        }
    }

    public bool Close()
    {
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
            OnError?.Invoke($"关闭串口失败: {ex.Message}");
            _port?.Dispose();
            _port = null;
            return false;
        }
    }

    public bool Send(string data, bool isHex = false)
    {
        if (_port?.IsOpen != true)
        {
            OnError?.Invoke("串口未打开");
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
                Id = Interlocked.Increment(ref _entryId),
                Timestamp = DateTime.Now,
                Direction = "TX",
                RawHex = isHex ? data.Replace(" ", "") : BitConverter.ToString(System.Text.Encoding.UTF8.GetBytes(data)).Replace("-", " "),
                Text = data
            };
            OnDataReceived?.Invoke(entry);
            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"发送失败: {ex.Message}");
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
            var buffer = new byte[_port.BytesToRead];
            _port.Read(buffer, 0, buffer.Length);

            var hex = BitConverter.ToString(buffer).Replace("-", " ");
            var text = System.Text.Encoding.UTF8.GetString(buffer);

            var entry = new LogEntry
            {
                Id = Interlocked.Increment(ref _entryId),
                Timestamp = DateTime.Now,
                Direction = "RX",
                RawHex = hex,
                Text = text
            };
            OnDataReceived?.Invoke(entry);
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"接收数据错误: {ex.Message}");
        }
    }

    private void OnSerialError(object sender, SerialErrorReceivedEventArgs e)
    {
        OnError?.Invoke($"串口错误: {e.EventType}");
        if (_port?.IsOpen != true)
        {
            OnDisconnected?.Invoke();
            StartAutoReconnect();
        }
    }

    public void EnableAutoReconnect(bool enable, int maxAttempts = 10, int delayMs = 1000)
    {
        _autoReconnect = enable;
        _reconnectMaxAttempts = maxAttempts;
        _reconnectDelayMs = delayMs;
        if (!enable)
        {
            _reconnectCts?.Cancel();
        }
    }

    private async void StartAutoReconnect()
    {
        if (!_autoReconnect || _lastConfig == null) return;
        _reconnectCts = new CancellationTokenSource();
        var token = _reconnectCts.Token;

        while (_reconnectAttempt < _reconnectMaxAttempts && !token.IsCancellationRequested)
        {
            await Task.Delay(_reconnectDelayMs, token);
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
                var msg = $"[自动重连成功] 第{_reconnectAttempt}次尝试";
                OnDataReceived?.Invoke(new LogEntry
                {
                    Id = Interlocked.Increment(ref _entryId),
                    Timestamp = DateTime.Now,
                    Direction = "RX",
                    RawHex = "",
                    Text = msg
                });
                return;
            }
            catch
            {
                if (_reconnectAttempt >= _reconnectMaxAttempts)
                {
                    OnError?.Invoke($"自动重连失败，已尝试 {_reconnectMaxAttempts} 次");
                }
            }
        }
    }

    public void Dispose()
    {
        Close();
    }
}
