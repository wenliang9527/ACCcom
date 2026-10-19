using System.Text;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public enum FrameExtractStrategy
{
    ByHeader,
    ByLengthField,
    FixedLength
}

public class FrameBufferConfig
{
    public FrameExtractStrategy Strategy { get; set; } = FrameExtractStrategy.ByHeader;
    public byte[]? Header { get; set; }
    public int LengthFieldOffset { get; set; } = -1;
    public int LengthFieldSize { get; set; } = 1;
    public int LengthFieldIncludes { get; set; } = 0;
    public bool LengthFieldBigEndian { get; set; } = false;
    public int FixedLength { get; set; }
    public int MaxFrameSize { get; set; } = 4096;
    public int BufferCapacity { get; set; } = 65536;
    public int PartialFrameTimeoutMs { get; set; } = 2000;
}

public class FrameBuffer : IDisposable
{
    private readonly FrameBufferConfig _config;
    private readonly AutoParserMatcher? _matcher;
    private readonly ParserManager? _parserManager;
    private byte[] _ringBuf;
    private int _head;
    private int _tail;
    private int _count;
    private readonly object _lock = new();
    private DateTime _lastDataTime;
    private Timer? _timeoutTimer;
    private volatile bool _disposed;
    private static int _entryIdSource;
    // Serializes parser execution so in-flight script runs cannot accumulate
    // without bound when parsing is slower than frame arrival, and so frames
    // complete (and surface via OnFrameAssembled) in arrival order.
    private readonly SemaphoreSlim _parseGate = new(1, 1);
    // Frames that have been extracted from the ring but not yet emitted. The
    // parse gate bounds concurrency, not depth: without this cap a slow parser
    // let every buffered frame be extracted up front, so each pending frame
    // pinned its own byte[] (unbounded with parser latency). Extracting at most
    // one frame at a time keeps the backlog inside the ring, which is bounded
    // by BufferCapacity.
    private const int MaxInFlightFrames = 1;
    private int _emitInFlight;

    public event Action<LogEntry>? OnFrameAssembled;
    public event Action<string>? OnError;

    public FrameBuffer(FrameBufferConfig config, AutoParserMatcher? matcher = null, ParserManager? parserManager = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        _config = config;
        _matcher = matcher;
        _parserManager = parserManager;
        // A zero/negative capacity would throw from the array allocation or
        // divide by zero in the ring wrap (_head % _ringBuf.Length). Clamp to a
        // minimum so the assembler stays usable with any config.
        _ringBuf = new byte[Math.Max(1, config.BufferCapacity)];
        _timeoutTimer = new Timer(CheckTimeout, null, 200, 200);
    }

    public void Write(byte[] data, int offset, int count)
    {
        if (_disposed || count == 0) return;
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (offset < 0 || count < 0 || offset + count > data.Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "offset/count exceed the data buffer");

        lock (_lock)
        {
            if (count >= _ringBuf.Length)
            {
                // Bulk input larger than the ring: keep only the newest bytes.
                offset += count - _ringBuf.Length;
                count = _ringBuf.Length;
                _head = 0;
                _tail = 0;
                _count = 0;
            }

            if (_count + count > _ringBuf.Length)
            {
                // Overflow: drop oldest bytes so the newest stay.
                int discard = _count + count - _ringBuf.Length;
                _head = (_head + discard) % _ringBuf.Length;
                _count -= discard;
            }

            int first = Math.Min(count, _ringBuf.Length - _tail);
            Buffer.BlockCopy(data, offset, _ringBuf, _tail, first);
            if (first < count)
                Buffer.BlockCopy(data, offset + first, _ringBuf, 0, count - first);

            _tail = (_tail + count) % _ringBuf.Length;
            _count += count;
            _lastDataTime = DateTime.UtcNow;
        }

        ProcessBuffer();
    }

    public void Write(byte[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        Write(data, 0, data.Length);
    }

    public void Reset()
    {
        lock (_lock)
        {
            _head = 0;
            _tail = 0;
            _count = 0;
        }
    }

    private void ProcessBuffer()
    {
        while (true)
        {
            if (_disposed) return;

            byte[]? frame;
            lock (_lock)
            {
                if (_count == 0) return;
                // Backpressure: a frame that is still being parsed keeps the
                // rest of the bytes in the ring. They are bounded by
                // BufferCapacity (drop-oldest on overflow), so memory no longer
                // grows with parser latency.
                if (Volatile.Read(ref _emitInFlight) >= MaxInFlightFrames) return;

                frame = TryExtractFrame();
                if (frame == null) return;
                _emitInFlight++;
            }

            var emit = EmitFrame(frame);
            if (emit.IsCompleted)
            {
                // No parser awaited, so the frame was emitted inline: keep
                // draining iteratively (a burst must never grow the stack).
                continue;
            }

            // The parse suspended. Resume draining when the in-flight frame
            // finally completes. EmitFrame catches its own exceptions, and the
            // continuation reads Exception so a fault cannot surface later as
            // an unobserved task exception.
            _ = emit.ContinueWith(
                static (task, state) =>
                {
                    _ = task.Exception;
                    ((FrameBuffer)state!).ProcessBuffer();
                },
                this, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return;
        }
    }

    private byte[]? TryExtractFrame() => _config.Strategy switch
    {
        FrameExtractStrategy.ByHeader => ExtractByHeader(),
        FrameExtractStrategy.ByLengthField => ExtractByLengthField(),
        FrameExtractStrategy.FixedLength => ExtractFixedLength(),
        _ => null
    };

    private byte[]? ExtractByHeader()
    {
        if (_config.Header == null || _config.Header.Length == 0)
        {
            if (_count == 0) return null;
            return ConsumeBytes(_count);
        }

        int headerPos = FindHeader();
        if (headerPos < 0)
        {
            if (_count > _config.Header.Length)
            {
                int discard = _count - _config.Header.Length + 1;
                Advance(discard);
            }
            return null;
        }

        if (headerPos > 0)
            Advance(headerPos);

        if (_config.LengthFieldOffset >= 0 && _count > _config.LengthFieldOffset + _config.LengthFieldSize)
        {
            int lenFieldValue = ReadLengthField(_config.LengthFieldOffset);
            int frameLen = lenFieldValue + _config.LengthFieldIncludes;
            if (frameLen > _config.MaxFrameSize) { Reset(); return null; }
            if (frameLen > 0 && _count >= frameLen) return ConsumeBytes(frameLen);
        }
        else if (_config.LengthFieldOffset < 0)
        {
            // No length field: everything from the header to the current end of
            // the buffered data is one frame (matches the legacy assembler's
            // empty-length-field semantics).
            return ConsumeBytes(_count);
        }

        return null;
    }

    private byte[]? ExtractByLengthField()
    {
        if (_config.LengthFieldOffset < 0)
            return null;
        if (_count < _config.LengthFieldOffset + _config.LengthFieldSize)
            return null;

        int frameLen = ReadLengthField(_config.LengthFieldOffset);
        if (frameLen <= 0 || frameLen > _config.MaxFrameSize) { if (frameLen > _config.MaxFrameSize) Reset(); return null; }
        if (_count >= frameLen) return ConsumeBytes(frameLen);
        return null;
    }

    private byte[]? ExtractFixedLength()
    {
        if (_config.FixedLength <= 0) return null;
        if (_count >= _config.FixedLength) return ConsumeBytes(_config.FixedLength);
        return null;
    }

    private int FindHeader()
    {
        var header = _config.Header!;
        int searchLen = _count - header.Length + 1;
        for (int i = 0; i < searchLen; i++)
        {
            bool match = true;
            for (int j = 0; j < header.Length; j++)
            {
                if (Peek(i + j) != header[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }

    private byte Peek(int offset) => _ringBuf[(_head + offset) % _ringBuf.Length];

    private int ReadLengthField(int offset)
    {
        if (_config.LengthFieldSize == 1) return Peek(offset);
        if (_config.LengthFieldSize == 2)
        {
            return _config.LengthFieldBigEndian
                ? (Peek(offset) << 8) | Peek(offset + 1)
                : Peek(offset) | (Peek(offset + 1) << 8);
        }
        return 0;
    }

    private byte[] ConsumeBytes(int count)
    {
        var result = new byte[count];
        int first = Math.Min(count, _ringBuf.Length - _head);
        Buffer.BlockCopy(_ringBuf, _head, result, 0, first);
        if (first < count)
            Buffer.BlockCopy(_ringBuf, 0, result, first, count - first);
        _head = (_head + count) % _ringBuf.Length;
        _count -= count;
        return result;
    }

    private void Advance(int count)
    {
        _head = (_head + count) % _ringBuf.Length;
        _count -= count;
    }

    private async Task EmitFrame(byte[] frame)
    {
        try
        {
            var hex = HexHelper.BytesToHexSpaced(frame, 0, frame.Length);
            var text = Encoding.UTF8.GetString(frame);

            var entry = new LogEntry
            {
                // Display/export row number for the assembled frame. The HTTP/MCP
                // polling cursor is DataBufferService's own arrival sequence, so
                // this id no longer has to be globally monotonic — it only has to
                // be unique among displayed rows.
                Id = Interlocked.Increment(ref _entryIdSource),
                // Local time, matching every other receive path (SerialService,
                // ModbusService, NetworkBridgeService) so exports/replay/PCAP from
                // this path don't carry a different timezone than the rest.
                Timestamp = DateTime.Now,
                Direction = "RX",
                RawHex = hex,
                Text = text,
                ByteCount = frame.Length
            };

            try
            {
                if (_matcher != null)
                {
                    var matchedParser = _matcher.MatchParser(frame);
                    if (matchedParser != null && _parserManager != null)
                    {
                        if (_parserManager.ActiveParserName != matchedParser)
                            _parserManager.Activate(matchedParser);
                    }
                }

                if (_parserManager?.ActiveParserName != null)
                {
                    // One parser run at a time: keeps completions in arrival order and
                    // bounds in-flight script executions when the parser is slower
                    // than frame arrival.
                    await _parseGate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        entry.Fields = await _parserManager.Engine.ExecuteAsync(frame, entry.Timestamp).ConfigureAwait(false);
                    }
                    finally
                    {
                        _parseGate.Release();
                    }
                }

                try { OnFrameAssembled?.Invoke(entry); }
                catch (Exception ex) { OnError?.Invoke($"FrameBuffer callback error: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"FrameBuffer error: {ex.Message}");
            }
        }
        finally
        {
            // Releases the backlog slot, so it must run for every exit path —
            // otherwise a frame that failed early would stall the drain and the
            // remaining frames would never leave the ring.
            Interlocked.Decrement(ref _emitInFlight);
        }
    }

    private void CheckTimeout(object? state)
    {
        lock (_lock)
        {
            if (_count == 0) return;
            // While a frame is in flight the bytes left in the ring are frames
            // queued behind the parser, not a stale partial frame: discarding
            // them would drop complete frames that simply had not been parsed
            // yet. The parser's own execution timeout bounds how long this can
            // defer a reset.
            if (Volatile.Read(ref _emitInFlight) > 0) return;
            if ((DateTime.UtcNow - _lastDataTime).TotalMilliseconds >= _config.PartialFrameTimeoutMs)
                Reset();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timeoutTimer?.Dispose();
        _timeoutTimer = null;
        _parseGate.Dispose();
    }
}
