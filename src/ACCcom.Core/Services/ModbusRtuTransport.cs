using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class ModbusRtuTransport : IModbusTransport
{
    private readonly ISerialService _serial;
    private readonly object _lock = new();
    private readonly Dictionary<string, TaskCompletionSource<byte[]>> _pending = new();
    private readonly Action<LogEntry> _dataHandler;
    private bool _disposed;

    public ModbusRtuTransport(ISerialService serial)
    {
        _serial = serial;
        _dataHandler = OnSerialData;
        _serial.OnDataReceived += _dataHandler;
    }

    public async Task<byte[]> SendReceiveAsync(byte slaveId, byte functionCode, byte[] pdu, int timeoutMs, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var adu = BuildAdu(slaveId, functionCode, pdu);
        var hex = BytesToHex(adu);
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

            if (!_serial.Send(hex, isHex: true))
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
        var bytes = HexStringToBytes(entry.RawHex);
        if (bytes.Length < 4) return;

        var slaveId = bytes[0];
        var funcByte = bytes[1];
        var function = funcByte & 0x7F;
        var key = MakeKey(slaveId, (byte)function);

        TaskCompletionSource<byte[]>? tcs;
        lock (_lock)
        {
            if (!_pending.TryGetValue(key, out var p)) return;
            _pending.Remove(key);
            tcs = p;
        }

        if ((funcByte & 0x80) != 0)
        {
            tcs.TrySetResult(bytes.AsSpan(0, bytes.Length - 2).ToArray());
            return;
        }

        var receivedCrc = (ushort)(bytes[^2] | (bytes[^1] << 8));
        var computedCrc = CrcHelper.Crc16(bytes.AsSpan(0, bytes.Length - 2));
        if (receivedCrc != computedCrc)
        {
            tcs.TrySetException(new InvalidOperationException($"CRC mismatch: received 0x{receivedCrc:X4}, computed 0x{computedCrc:X4}"));
            return;
        }

        tcs.TrySetResult(bytes.AsSpan(0, bytes.Length - 2).ToArray());
    }

    private static byte[] BuildAdu(byte slaveId, byte functionCode, byte[] pdu)
    {
        var adu = new byte[1 + pdu.Length + 2];
        adu[0] = slaveId;
        adu[1] = functionCode;
        Array.Copy(pdu, 0, adu, 2, pdu.Length);
        var crc = CrcHelper.Crc16(adu.AsSpan(0, adu.Length - 2));
        adu[^2] = (byte)(crc & 0xFF);
        adu[^1] = (byte)((crc >> 8) & 0xFF);
        return adu;
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

    internal static byte[] BuildMaskWriteRequest(ushort addr, ushort andMask, ushort orMask)
    {
        return
        [
            (byte)(addr >> 8), (byte)addr,
            (byte)(andMask >> 8), (byte)andMask,
            (byte)(orMask >> 8), (byte)orMask
        ];
    }

    internal static byte[] BuildReadWriteRegistersRequest(ushort readAddr, ushort readCount, ushort writeAddr, ushort[] writeValues)
    {
        var pdu = new byte[9 + writeValues.Length * 2];
        pdu[0] = (byte)(readAddr >> 8); pdu[1] = (byte)readAddr;
        pdu[2] = (byte)(readCount >> 8); pdu[3] = (byte)readCount;
        pdu[4] = (byte)(writeAddr >> 8); pdu[5] = (byte)writeAddr;
        pdu[6] = (byte)(writeValues.Length >> 8); pdu[7] = (byte)writeValues.Length;
        pdu[8] = (byte)(writeValues.Length * 2);
        for (int i = 0; i < writeValues.Length; i++)
        {
            pdu[9 + i * 2] = (byte)(writeValues[i] >> 8);
            pdu[9 + i * 2 + 1] = (byte)writeValues[i];
        }
        return pdu;
    }

    internal static byte[] BuildReadRequest(ushort startAddr, ushort count)
        => [(byte)(startAddr >> 8), (byte)startAddr, (byte)(count >> 8), (byte)count];

    internal static byte[] BuildWriteCoilRequest(ushort addr, bool value)
        => [(byte)(addr >> 8), (byte)addr, value ? (byte)0xFF : (byte)0x00, (byte)0x00];

    internal static byte[] BuildWriteRegisterRequest(ushort addr, ushort value)
        => [(byte)(addr >> 8), (byte)addr, (byte)(value >> 8), (byte)value];

    internal static byte[] BuildWriteCoilsRequest(ushort startAddr, bool[] values)
    {
        var byteCount = (values.Length + 7) / 8;
        var coils = new byte[byteCount];
        for (int i = 0; i < values.Length; i++)
            if (values[i]) coils[i / 8] |= (byte)(1 << (i % 8));

        var pdu = new byte[5 + byteCount];
        pdu[0] = (byte)(startAddr >> 8);
        pdu[1] = (byte)startAddr;
        pdu[2] = (byte)(values.Length >> 8);
        pdu[3] = (byte)values.Length;
        pdu[4] = (byte)byteCount;
        Array.Copy(coils, 0, pdu, 5, byteCount);
        return pdu;
    }

    internal static byte[] BuildWriteRegistersRequest(ushort startAddr, ushort[] values)
    {
        var pdu = new byte[5 + values.Length * 2];
        pdu[0] = (byte)(startAddr >> 8);
        pdu[1] = (byte)startAddr;
        pdu[2] = (byte)(values.Length >> 8);
        pdu[3] = (byte)values.Length;
        pdu[4] = (byte)(values.Length * 2);
        for (int i = 0; i < values.Length; i++)
        {
            pdu[5 + i * 2] = (byte)(values[i] >> 8);
            pdu[5 + i * 2 + 1] = (byte)values[i];
        }
        return pdu;
    }

    private static string BytesToHex(byte[] data)
        => HexHelper.BytesToHexSpaced(data, 0, data.Length);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _serial.OnDataReceived -= _dataHandler;
        lock (_lock)
        {
            foreach (var kv in _pending)
                kv.Value.TrySetException(new ObjectDisposedException(nameof(ModbusRtuTransport)));
            _pending.Clear();
        }
    }
}
