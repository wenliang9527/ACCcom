using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class ModbusAsciiTransport : IModbusTransport
{
    private readonly ISerialService _serial;
    private readonly object _lock = new();
    private readonly Dictionary<string, TaskCompletionSource<byte[]>> _pending = new();
    private readonly Action<LogEntry> _dataHandler;
    private readonly object _rxLock = new();
    private readonly List<byte> _rxBuffer = new();
    private bool _disposed;

    public ModbusAsciiTransport(ISerialService serial)
    {
        _serial = serial;
        _dataHandler = OnSerialData;
        _serial.OnDataReceived += _dataHandler;
    }

    public async Task<byte[]> SendReceiveAsync(byte slaveId, byte functionCode, byte[] pdu, int timeoutMs, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var adu = BuildAdu(slaveId, functionCode, pdu);
        var asciiFrame = FormatAsciiFrame(adu);
        var key = MakeKey(slaveId, functionCode);
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock) { _pending[key] = tcs; }

        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ct);
            using var registration = linkedCts.Token.Register(() =>
            {
                lock (_lock) { if (_pending.TryGetValue(key, out var p) && p == tcs) _pending.Remove(key); }
                tcs.TrySetException(new OperationCanceledException(ct.IsCancellationRequested ? "Operation cancelled" : $"Timeout after {timeoutMs}ms"));
            }, useSynchronizationContext: false);

            if (!_serial.Send(asciiFrame, isHex: false))
            {
                lock (_lock) _pending.Remove(key);
                throw new InvalidOperationException("Send failed");
            }

            return await tcs.Task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            lock (_lock) _pending.Remove(key);
            throw;
        }
    }

    private void OnSerialData(LogEntry entry)
    {
        if (entry.Direction != "RX") return;
        if (string.IsNullOrEmpty(entry.RawHex)) return;

        var rawBytes = HexStringToBytes(entry.RawHex);

        byte[]? completeAscii = null;
        lock (_rxLock)
        {
            _rxBuffer.AddRange(rawBytes);
            int crIndex = -1;
            for (int i = 0; i < _rxBuffer.Count; i++)
            {
                if (_rxBuffer[i] == 0x0D)
                {
                    crIndex = i;
                    break;
                }
            }
            if (crIndex < 0) return;

            int end = crIndex + 1;
            if (end < _rxBuffer.Count && _rxBuffer[end] == 0x0A)
                end++;
            completeAscii = _rxBuffer.GetRange(0, end).ToArray();
            _rxBuffer.RemoveRange(0, end);
        }

        if (completeAscii == null) return;

        var asciiStr = System.Text.Encoding.ASCII.GetString(completeAscii);
        int colonIndex = asciiStr.IndexOf(':');
        if (colonIndex < 0) return;

        int crIdx = asciiStr.IndexOf('\r');
        if (crIdx < 0) crIdx = asciiStr.IndexOf('\n');
        if (crIdx < 0) return;

        var hexContent = asciiStr.Substring(colonIndex + 1, crIdx - colonIndex - 1).Replace(" ", "");
        var data = HexStringToBytes(hexContent);
        if (data.Length < 4) return;

        var slaveId = data[0];
        var funcByte = data[1];
        var function = funcByte & 0x7F;
        var key = MakeKey(slaveId, (byte)function);

        TaskCompletionSource<byte[]>? tcs;
        lock (_lock)
        {
            if (!_pending.TryGetValue(key, out var p)) return;
            _pending.Remove(key);
            tcs = p;
        }

        var lrcData = data.AsSpan(0, data.Length - 1);
        var receivedLrc = data[^1];
        var computedLrc = CalculateLrc(lrcData);
        if (receivedLrc != computedLrc)
        {
            tcs.TrySetException(new InvalidOperationException($"LRC mismatch: received 0x{receivedLrc:X2}, computed 0x{computedLrc:X2}"));
            return;
        }

        tcs.TrySetResult(data.AsSpan(0, data.Length - 1).ToArray());
    }

    internal static byte CalculateLrc(ReadOnlySpan<byte> data)
    {
        byte lrc = 0;
        foreach (var b in data)
            lrc += b;
        return (byte)((~lrc) + 1);
    }

    private static byte[] BuildAdu(byte slaveId, byte functionCode, byte[] pdu)
    {
        var adu = new byte[1 + pdu.Length + 1];
        adu[0] = slaveId;
        adu[1] = functionCode;
        Array.Copy(pdu, 0, adu, 2, pdu.Length);
        adu[^1] = CalculateLrc(adu.AsSpan(0, adu.Length - 1));
        return adu;
    }

    internal static string FormatAsciiFrame(byte[] adu)
    {
        var sb = new System.Text.StringBuilder(adu.Length * 2 + 3);
        sb.Append(':');
        foreach (var b in adu)
            sb.Append(b.ToString("X2"));
        sb.Append('\r');
        sb.Append('\n');
        return sb.ToString();
    }

    private static string MakeKey(byte slaveId, byte functionCode) => $"{slaveId}:{functionCode}";

    internal static byte[] HexStringToBytes(string hex)
    {
        int nonSpaceLen = 0;
        foreach (var c in hex.AsSpan())
            if (c != ' ') nonSpaceLen++;
        var bytes = new byte[nonSpaceLen / 2];
        int byteIdx = 0, hi = -1;
        foreach (var c in hex.AsSpan())
        {
            if (c == ' ') continue;
            int val = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'A' and <= 'F' => c - 'A' + 10,
                >= 'a' and <= 'f' => c - 'a' + 10,
                _ => 0
            };
            if (hi < 0) hi = val;
            else { bytes[byteIdx++] = (byte)(hi << 4 | val); hi = -1; }
        }
        return bytes;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _serial.OnDataReceived -= _dataHandler;
        lock (_lock)
        {
            foreach (var kv in _pending)
                kv.Value.TrySetException(new ObjectDisposedException(nameof(ModbusAsciiTransport)));
            _pending.Clear();
        }
    }
}
