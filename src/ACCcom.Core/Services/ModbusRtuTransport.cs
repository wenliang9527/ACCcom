using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class ModbusRtuTransport : IModbusTransport
{
    private readonly ISerialService _serial;
    private readonly object _lock = new();
    private readonly Dictionary<ModbusKey, Queue<TaskCompletionSource<byte[]>>> _pending = new();
    // Number of keys with a live queue in _pending. Read on the RX hot path
    // WITHOUT the lock: when it is 0 no conversation is waiting, so the packet
    // is dropped before the hex decode and key work (no allocation, no lock).
    // A key is added when a queue is created and removed when a queue empties,
    // so a stale non-zero value only costs the full path (which then finds an
    // empty queue and returns) — never a wrong answer.
    private volatile int _keyCount;
    private readonly Action<LogEntry> _dataHandler;
    private bool _disposed;

    public ModbusRtuTransport(ISerialService serial)
    {
        ArgumentNullException.ThrowIfNull(serial);
        _serial = serial;
        _dataHandler = OnSerialData;
        _serial.OnDataReceived += _dataHandler;
    }

    public async Task<byte[]> SendReceiveAsync(byte slaveId, byte functionCode, byte[] pdu, int timeoutMs, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(pdu);
        var adu = BuildAdu(slaveId, functionCode, pdu);
        var hex = BytesToHex(adu);
        var key = new ModbusKey(slaveId, functionCode);
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            if (!_pending.TryGetValue(key, out var queue))
            {
                queue = new Queue<TaskCompletionSource<byte[]>>();
                _pending[key] = queue;
                Interlocked.Increment(ref _keyCount);
            }
            queue.Enqueue(tcs);
        }

        try
        {
            // CancellationTokenSource throws for non-positive due-times; clamp so
            // a non-positive timeout behaves as "immediate timeout" instead of
            // surfacing an ArgumentOutOfRangeException to the caller.
            var effectiveTimeout = Math.Max(1, timeoutMs);
            using var cts = new CancellationTokenSource(effectiveTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ct);
            using var registration = linkedCts.Token.Register(() =>
            {
                lock (_lock) RemoveHeadLocked(key, tcs);
                tcs.TrySetException(new OperationCanceledException(ct.IsCancellationRequested ? "Operation cancelled" : $"Timeout after {timeoutMs}ms"));
            }, useSynchronizationContext: false);

            if (!_serial.Send(hex, isHex: true))
            {
                lock (_lock) RemoveHeadLocked(key, tcs);
                throw new InvalidOperationException("Send failed");
            }

            return await tcs.Task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            lock (_lock) RemoveHeadLocked(key, tcs);
            throw;
        }
    }

    /// <summary>Caller holds <see cref="_lock"/>. Removes <paramref name="tcs"/>
    /// when it is the head of its queue, and drops the key when the queue
    /// empties — keeping the RX fast path (<c>_keyCount == 0</c>) accurate
    /// after timeouts and send failures.</summary>
    private void RemoveHeadLocked(ModbusKey key, TaskCompletionSource<byte[]> tcs)
    {
        if (_pending.TryGetValue(key, out var q) && q.Count > 0 && q.Peek() == tcs)
        {
            q.Dequeue();
            if (q.Count == 0)
            {
                _pending.Remove(key);
                Interlocked.Decrement(ref _keyCount);
            }
        }
    }

    private void OnSerialData(LogEntry? entry)
    {
        if (entry == null || entry.Direction != "RX") return;
        if (string.IsNullOrEmpty(entry.RawHex)) return;
        // RX hot path: with no conversation pending (the common case for a
        // port that only occasionally runs Modbus master polls) drop the
        // packet before decoding the hex string or building a lookup key —
        // the old path allocated a byte[] and a key string per packet and
        // then discovered nothing was waiting.
        if (_keyCount == 0) return;
        var bytes = HexStringToBytes(entry.RawHex);
        if (bytes.Length < 4) return;

        var slaveId = bytes[0];
        var funcByte = bytes[1];
        var function = funcByte & 0x7F;
        var key = new ModbusKey(slaveId, (byte)function);

        TaskCompletionSource<byte[]>? tcs;
        lock (_lock)
        {
            if (!_pending.TryGetValue(key, out var queue) || queue.Count == 0) return;
            tcs = queue.Dequeue();
            if (queue.Count == 0)
            {
                _pending.Remove(key);
                Interlocked.Decrement(ref _keyCount);
            }
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

    internal static byte[] BuildAdu(byte slaveId, byte functionCode, byte[] pdu)
    {
        // slaveId (1) + functionCode (1) + pdu + CRC (2). The size matters:
        // a one-byte-short buffer made adu[^2] (CRC low) overwrite the PDU's
        // last byte and the CRC cover the wrong range — real devices rejected
        // every RTU frame (the malformed frame was invisible to the virtual
        // serial tests, which never assert the sent request bytes).
        var adu = new byte[2 + pdu.Length + 2];
        adu[0] = slaveId;
        adu[1] = functionCode;
        Array.Copy(pdu, 0, adu, 2, pdu.Length);
        var crc = CrcHelper.Crc16(adu.AsSpan(0, adu.Length - 2));
        adu[^2] = (byte)(crc & 0xFF);
        adu[^1] = (byte)((crc >> 8) & 0xFF);
        return adu;
    }

    /// <summary>Lenient hex parse (illegal nibbles read as 0, odd trailing
    /// nibble dropped) — byte-for-byte the shared HexHelper contract, which the
    /// master, ASCII and slave transports all used to carry their own verbatim
    /// copy of.</summary>
    internal static byte[] HexStringToBytes(string hex) => HexHelper.HexStringToBytes(hex);

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
                foreach (var tcs in kv.Value)
                    tcs.TrySetException(new ObjectDisposedException(nameof(ModbusRtuTransport)));
            _pending.Clear();
            Interlocked.Exchange(ref _keyCount, 0);
        }
    }
}
