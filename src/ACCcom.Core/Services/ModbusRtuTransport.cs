using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class ModbusRtuTransport : IModbusTransport
{
    private readonly ISerialService _serial;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _busGate = new(1, 1);
    private readonly Dictionary<ModbusKey, Queue<Conversation>> _pending = new();
    // Number of keys with a live queue in _pending. Read on the RX hot path
    // WITHOUT the lock: when it is 0 no conversation is waiting, so the packet
    // is dropped before the hex decode and key work (no allocation, no lock).
    // A key is added when a queue is created and removed when a queue empties,
    // so a stale non-zero value only costs the full path (which then finds an
    // empty queue and returns) — never a wrong answer.
    private volatile int _keyCount;
    private readonly Action<LogEntry> _dataHandler;
    private bool _disposed;

    /// <summary>RTU ADUs are at most 256 bytes. A per-conversation RX buffer
    /// that grows past this holds noise, not a split response — fail the
    /// pending request immediately instead of waiting for its full timeout.</summary>
    private const int MaxResponseBytes = 256;

    public ModbusRtuTransport(ISerialService serial)
    {
        ArgumentNullException.ThrowIfNull(serial);
        _serial = serial;
        _dataHandler = OnSerialData;
        _serial.OnDataReceived += _dataHandler;
    }

    /// <summary>One in-flight request-response conversation. The buffer
    /// accumulates RX chunks until a CRC-aligned frame can be extracted — a
    /// response split across serial deliveries (or preceded by the tail of a
    /// previous timed-out response) used to be CRC-checked per chunk and
    /// failed with a spurious mismatch.</summary>
    private sealed class Conversation
    {
        public Conversation(ModbusKey key) { Key = key; }

        public ModbusKey Key { get; }
        public TaskCompletionSource<byte[]> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<byte> Buffer { get; } = new();
    }

    public async Task<byte[]> SendReceiveAsync(byte slaveId, byte functionCode, byte[] pdu, int timeoutMs, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(pdu);
        var adu = BuildAdu(slaveId, functionCode, pdu);
        var hex = BytesToHex(adu);
        var key = new ModbusKey(slaveId, functionCode);

        // Half-duplex bus gate: RTU carries one transaction at a time.
        // Concurrent polls (e.g. scanner + manual read) used to interleave two
        // requests on the wire and let each wait out the other's response.
        // The per-request timeout starts after acquisition — a queued request
        // still gets its full response budget; ct cancels the queue wait.
        await _busGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var conversation = new Conversation(key);

            lock (_lock)
            {
                if (!_pending.TryGetValue(key, out var queue))
                {
                    queue = new Queue<Conversation>();
                    _pending[key] = queue;
                    Interlocked.Increment(ref _keyCount);
                }
                queue.Enqueue(conversation);
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
                    lock (_lock) RemoveConversationLocked(key, conversation);
                    conversation.Tcs.TrySetException(new OperationCanceledException(ct.IsCancellationRequested ? "Operation cancelled" : $"Timeout after {timeoutMs}ms"));
                }, useSynchronizationContext: false);

                if (!_serial.Send(hex, isHex: true))
                {
                    lock (_lock) RemoveConversationLocked(key, conversation);
                    throw new InvalidOperationException("Send failed");
                }

                return await conversation.Tcs.Task.ConfigureAwait(false);
            }
            catch
            {
                lock (_lock) RemoveConversationLocked(key, conversation);
                throw;
            }
        }
        finally
        {
            _busGate.Release();
        }
    }

    /// <summary>Caller holds <see cref="_lock"/>. Removes <paramref name="conversation"/>
    /// when it is the head of its queue, and drops the key when the queue
    /// empties — keeping the RX fast path (<c>_keyCount == 0</c>) accurate
    /// after timeouts and send failures.</summary>
    private void RemoveConversationLocked(ModbusKey key, Conversation conversation)
    {
        if (!_pending.TryGetValue(key, out var q)) return;
        if (q.Count == 0 || !ReferenceEquals(q.Peek(), conversation)) return;
        q.Dequeue();
        if (q.Count == 0)
        {
            _pending.Remove(key);
            Interlocked.Decrement(ref _keyCount);
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
        if (bytes.Length == 0) return;

        lock (_lock)
        {
            Conversation? conversation;
            ModbusKey key;
            if (bytes.Length >= 2)
            {
                // Chunk starts with a (slave, function) pair: a frame boundary.
                key = new ModbusKey(bytes[0], (byte)(bytes[1] & 0x7F));
                if (_pending.TryGetValue(key, out var queue) && queue.Count > 0)
                {
                    conversation = queue.Peek();
                }
                else if (_pending.Count == 0)
                {
                    return;
                }
                else if (HasCrcValidFrame(bytes))
                {
                    // A complete, CRC-valid frame for some OTHER key: a late
                    // reply to an already-timed-out request — drop it (the old
                    // per-chunk key routing did too).
                    return;
                }
                else
                {
                    // Mid-frame continuation of the live transaction (the bus
                    // gate keeps at most one): its leading bytes are frame
                    // payload, not a header — append to the pending
                    // conversation instead of misrouting on a fake key.
                    conversation = _pending.Values.SelectMany(q => q).FirstOrDefault();
                    if (conversation == null) return;
                    key = conversation.Key;
                }
            }
            else
            {
                // A 1-byte chunk cannot name a (slave, function) key — same
                // live-transaction fallback.
                conversation = _pending.Values.SelectMany(q => q).FirstOrDefault();
                if (conversation == null) return;
                key = conversation.Key;
            }

            // Accumulate: the buffer holds chunks until a CRC-aligned frame
            // can be extracted (split responses, or leading noise bytes).
            conversation.Buffer.AddRange(bytes);

            if (conversation.Buffer.Count > MaxResponseBytes)
            {
                RemoveConversationLocked(key, conversation);
                conversation.Tcs.TrySetException(new InvalidOperationException(
                    $"No valid RTU frame in {conversation.Buffer.Count} bytes (CRC-aligned search failed)"));
                return;
            }

            // RTU has no length field: scan longest-first for a suffix whose
            // last two bytes are a valid CRC over the prefix. A complete frame
            // completes the request; a partial one keeps waiting — each serial
            // chunk re-runs the scan, so a split response assembles as it
            // arrives. Exception responses (func | 0x80) are CRC-checked like
            // any other frame here; the old path validated them never.
            if (!TryExtractCrcAlignedFrame(conversation.Buffer, out var frame)) return;

            RemoveConversationLocked(key, conversation);
            // The caller sees the ADU without the CRC trailer.
            conversation.Tcs.TrySetResult(frame[..^2]);
        }
    }

    /// <summary>True when the span contains at least one CRC-valid frame.
    /// Used to tell a complete foreign reply (drop) from a mid-frame chunk
    /// (append to the live conversation).</summary>
    private static bool HasCrcValidFrame(ReadOnlySpan<byte> span)
    {
        for (var start = 0; start + 4 <= span.Length; start++)
        {
            for (var len = span.Length - start; len >= 4; len--)
            {
                var bodyEnd = start + len - 2;
                var received = (ushort)(span[bodyEnd] | (span[bodyEnd + 1] << 8));
                if (received == CrcHelper.Crc16(span.Slice(start, len - 2))) return true;
            }
        }
        return false;
    }

    /// <summary>Searches the buffer for the first CRC-valid frame (earliest
    /// start, then longest — a complete request wins over a coincidental
    /// 4-byte CRC match inside it). Returns false, leaving the buffer
    /// untouched, while no complete frame is present yet; on success the
    /// consumed bytes (including any leading noise) are removed.</summary>
    private static bool TryExtractCrcAlignedFrame(List<byte> buffer, out byte[] frame)
    {
        frame = Array.Empty<byte>();
        var count = buffer.Count;
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(buffer);
        for (var start = 0; start + 4 <= count; start++)
        {
            for (var len = count - start; len >= 4; len--)
            {
                var bodyEnd = start + len - 2;
                var received = (ushort)(span[bodyEnd] | (span[bodyEnd + 1] << 8));
                if (received != CrcHelper.Crc16(span.Slice(start, len - 2))) continue;
                frame = span.Slice(start, len).ToArray();
                if (start > 0)
                    buffer.RemoveRange(0, start);
                buffer.RemoveRange(0, len);
                return true;
            }
        }
        return false;
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
                foreach (var conversation in kv.Value)
                    conversation.Tcs.TrySetException(new ObjectDisposedException(nameof(ModbusRtuTransport)));
            _pending.Clear();
            Interlocked.Exchange(ref _keyCount, 0);
        }
        // _busGate is deliberately NOT disposed: a caller parked in
        // WaitAsync while Dispose runs would get an ObjectDisposedException
        // from the gate itself. A gate with no waiters holds no resources,
        // and host lifecycle keeps Dispose exclusive of new requests.
    }
}
