using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class ModbusRtuSlaveTransport : IDisposable
{
    private readonly ISerialService _serial;
    private readonly List<byte> _buffer = new();
    private readonly object _lock = new();
    private readonly Action<LogEntry> _dataHandler;
    private System.Timers.Timer? _frameTimer;
    private bool _disposed;
    private bool _isRunning;

    public byte SlaveId { get; set; } = 0x01;
    public bool IsRunning => _isRunning;
    public Func<byte, byte[], byte[]>? OnRequestReceived { get; set; }

    public ModbusRtuSlaveTransport(ISerialService serial)
    {
        _serial = serial;
        _dataHandler = OnSerialData;
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _frameTimer = new System.Timers.Timer(10);
        _frameTimer.AutoReset = false;
        _frameTimer.Elapsed += (_, _) => ProcessBuffer();
        _serial.OnDataReceived += _dataHandler;
    }

    public void Stop()
    {
        _isRunning = false;
        _serial.OnDataReceived -= _dataHandler;
        _frameTimer?.Stop();
        _frameTimer?.Dispose();
        _frameTimer = null;
        lock (_lock) _buffer.Clear();
    }

    private void OnSerialData(LogEntry entry)
    {
        if (!_isRunning || entry.Direction != "RX") return;
        if (string.IsNullOrEmpty(entry.RawHex)) return;
        var bytes = HexStringToBytes(entry.RawHex);
        lock (_lock) { _buffer.AddRange(bytes); }
        _frameTimer?.Stop();
        _frameTimer?.Start();
    }

    private void ProcessBuffer()
    {
        byte[] frame;
        lock (_lock)
        {
            if (_buffer.Count < 4) { _buffer.Clear(); return; }
            frame = _buffer.ToArray();
            _buffer.Clear();
        }
        var slaveId = frame[0];
        if (slaveId != SlaveId) return;
        var receivedCrc = (ushort)(frame[^2] | (frame[^1] << 8));
        var computedCrc = Crc16(frame.AsSpan(0, frame.Length - 2));
        if (receivedCrc != computedCrc) return;
        var handler = OnRequestReceived;
        if (handler == null) return;
        var pdu = frame[1..^2];
        byte[] responseBody;
        try { responseBody = handler(slaveId, pdu); }
        catch { return; }
        if (responseBody.Length == 0) return;
        var funcCode = pdu[0];
        var adu = new byte[1 + 1 + responseBody.Length + 2];
        adu[0] = slaveId;
        adu[1] = funcCode;
        Array.Copy(responseBody, 0, adu, 2, responseBody.Length);
        var crc = Crc16(adu.AsSpan(0, adu.Length - 2));
        adu[^2] = (byte)(crc & 0xFF);
        adu[^1] = (byte)((crc >> 8) & 0xFF);
        _serial.Send(string.Join(" ", adu.Select(b => b.ToString("X2"))), isHex: true);
    }

    private static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data) { crc ^= b; for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1); }
        return crc;
    }

    private static byte[] HexStringToBytes(string hex)
    {
        var cleaned = hex.Replace(" ", "");
        return Convert.FromHexString(cleaned);
    }

    public void Dispose() { if (_disposed) return; _disposed = true; Stop(); }
}
