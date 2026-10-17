using System.Runtime.InteropServices;
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

    /// <summary>Upper bound for the retained RX buffer. A valid RTU ADU is at
    /// most 256 bytes; a full buffer that yields no frame holds noise.</summary>
    private const int MaxBufferBytes = 512;

    public byte SlaveId { get; set; } = 0x01;
    public bool IsRunning => _isRunning;
    public string? LastError { get; private set; }
    public Func<byte, byte[], byte[]>? OnRequestReceived { get; set; }

    public ModbusRtuSlaveTransport(ISerialService serial)
    {
        ArgumentNullException.ThrowIfNull(serial);
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

    private void OnSerialData(LogEntry? entry)
    {
        if (!_isRunning || entry == null || entry.Direction != "RX") return;
        if (string.IsNullOrEmpty(entry.RawHex)) return;
        var bytes = HexStringToBytes(entry.RawHex);
        lock (_lock) { _buffer.AddRange(bytes); }
        _frameTimer?.Stop();
        _frameTimer?.Start();
    }

    private void ProcessBuffer()
    {
        while (true)
        {
            byte[] frame;
            lock (_lock)
            {
                // The buffer is bounded: with partial data now retained
                // between ticks, a stream of pure noise would otherwise grow
                // it without limit. A valid RTU ADU is at most 256 bytes, so
                // a full buffer that yields no frame holds noise, not a
                // split request.
                if (_buffer.Count > MaxBufferBytes) _buffer.Clear();
                if (_buffer.Count < 4) return;

                if (!TryTakeFrame(out frame))
                {
                    if (_buffer.Count >= MaxBufferBytes) _buffer.Clear();
                    return;
                }
            }

            // The frame is CRC-verified by TryTakeFrame. Everything below runs
            // outside the lock so a slow handler or serial write never blocks
            // the RX thread.
            var slaveId = frame[0];
            if (slaveId != SlaveId) continue;
            var handler = OnRequestReceived;
            if (handler == null) continue;
            var pdu = frame[1..^2];
            byte[] responseBody;
            try { responseBody = handler(slaveId, pdu); }
            catch (Exception ex)
            {
                LastError = $"Handler error: {ex.Message}";
                continue;
            }
            if (responseBody.Length == 0) continue;
            // The handler returns the full response PDU starting with the function
            // code (see ModbusSlaveDevice.WithFunctionCode), so the ADU is
            // [slave id][response PDU][CRC16].
            var adu = new byte[1 + responseBody.Length + 2];
            adu[0] = slaveId;
            Array.Copy(responseBody, 0, adu, 1, responseBody.Length);
            var crc = CrcHelper.Crc16(adu.AsSpan(0, adu.Length - 2));
            adu[^2] = (byte)(crc & 0xFF);
            adu[^1] = (byte)((crc >> 8) & 0xFF);
            _serial.Send(HexHelper.BytesToHexSpaced(adu, 0, adu.Length), isHex: true);
        }
    }

    /// <summary>
    /// Removes and returns the first CRC-aligned frame from the buffer,
    /// consuming any noise bytes in front of it. Returns false — leaving the
    /// buffer untouched — when no complete valid frame is present, so a
    /// request split across serial deliveries (the 10 ms frame timer firing
    /// mid-frame) can still complete on a later tick. The previous
    /// implementation treated the whole buffer as one frame and cleared it,
    /// which silently dropped split requests, back-to-back frames, and frames
    /// preceded by line noise.
    /// </summary>
    private bool TryTakeFrame(out byte[] frame)
    {
        frame = Array.Empty<byte>();
        var count = _buffer.Count;
        var span = CollectionsMarshal.AsSpan(_buffer);
        for (var start = 0; start + 4 <= count; start++)
        {
            // Longest candidate first, so a complete request wins over a
            // coincidental 4-byte CRC match inside it.
            for (var len = count - start; len >= 4; len--)
            {
                var bodyEnd = start + len - 2;
                var received = (ushort)(span[bodyEnd] | (span[bodyEnd + 1] << 8));
                if (received != CrcHelper.Crc16(span.Slice(start, len - 2))) continue;
                var taken = new byte[len];
                span.Slice(start, len).CopyTo(taken);
                _buffer.RemoveRange(0, start + len);
                frame = taken;
                return true;
            }
        }
        return false;
    }

    // 委托 RTU master transport 的容错解析:非法字符按 0 处理、奇数尾巴丢弃,
    // 与 ModbusRtuTransport/ModbusAsciiTransport 同一家族行为。此前的
    // Convert.FromHexString 会对脏/奇数长度 RX hex 抛 FormatException——
    // OnSerialData 无 try/catch,一帧脏数据就中断从站响应链路。
    internal static byte[] HexStringToBytes(string hex) => ModbusRtuTransport.HexStringToBytes(hex);

    public void Dispose() { if (_disposed) return; _disposed = true; Stop(); }
}
