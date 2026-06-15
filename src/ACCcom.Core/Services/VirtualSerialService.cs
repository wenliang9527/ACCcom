using System.Collections.Generic;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class VirtualSerialService : ISerialService, IDisposable
{
    private bool _isOpen;
    private string? _currentPort;
    private int _baudRate;
    private int _nextRxId;
    private int _nextTxId;

    private readonly List<LogEntry> _sentData = new();
    private readonly object _lock = new();

    public bool IsOpen => _isOpen;
    public string? CurrentPort => _currentPort;
    public int BaudRate => _baudRate;

    public event Action<LogEntry>? OnDataReceived;
    public event Action<string>? OnError;
    public event Action? OnDisconnected;

    public bool Open(SerialConfig config)
    {
        _currentPort = config.PortName;
        _baudRate = config.BaudRate;
        _isOpen = true;
        return true;
    }

    public bool Close()
    {
        _isOpen = false;
        _currentPort = null;
        _baudRate = 0;
        OnDisconnected?.Invoke();
        return true;
    }

    public bool Send(string data, bool isHex = false)
    {
        if (!_isOpen)
        {
            OnError?.Invoke("Serial port not open");
            return false;
        }

        var textBytes = System.Text.Encoding.UTF8.GetBytes(data);
        var hexStr = isHex ? data.Replace(" ", "") :
            BitConverter.ToString(textBytes).Replace("-", " ");

        var entry = new LogEntry
        {
            Id = Interlocked.Increment(ref _nextTxId),
            Timestamp = DateTime.Now,
            Direction = "TX",
            RawHex = hexStr,
            Text = data
        };

        lock (_lock) _sentData.Add(entry);
        OnDataReceived?.Invoke(entry);
        return true;
    }

    public bool SendHex(string hex) => Send(hex, true);

    public void InjectRxData(string hex)
    {
        var hexNoSpace = hex.Replace(" ", "");
        var bytes = Convert.FromHexString(hexNoSpace);

        var entry = new LogEntry
        {
            Id = Interlocked.Increment(ref _nextRxId),
            Timestamp = DateTime.Now,
            Direction = "RX",
            RawHex = BitConverter.ToString(bytes).Replace("-", " "),
            Text = System.Text.Encoding.UTF8.GetString(bytes)
        };

        OnDataReceived?.Invoke(entry);
    }

    public List<LogEntry> GetSentData()
    {
        lock (_lock) return new List<LogEntry>(_sentData);
    }

    public int SentCount
    {
        get { lock (_lock) return _sentData.Count; }
    }

    public void ClearSentData()
    {
        lock (_lock) _sentData.Clear();
    }

    public void EnableAutoReconnect(bool enable, int maxAttempts = 10, int delayMs = 1000) { }
    public void UpdateReconnectSettings(ReconnectSettings settings) { }
    public void Dispose() { }
}
