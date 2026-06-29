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
    private bool _disposed;

    public event Action<LogEntry>? OnFrameAssembled;
    public event Action<string>? OnError;

    public int DataCount
    {
        get { lock (_lock) return _count; }
    }

    public FrameBuffer(FrameBufferConfig config, AutoParserMatcher? matcher = null, ParserManager? parserManager = null)
    {
        _config = config;
        _matcher = matcher;
        _parserManager = parserManager;
        _ringBuf = new byte[config.BufferCapacity];
        _timeoutTimer = new Timer(CheckTimeout, null, 200, 200);
    }

    public void Write(byte[] data, int offset, int count)
    {
        if (_disposed || count == 0) return;

        lock (_lock)
        {
            for (int i = 0; i < count; i++)
            {
                if (_count >= _ringBuf.Length)
                {
                    _head = (_head + 1) % _ringBuf.Length;
                    _count--;
                }
                _ringBuf[_tail] = data[offset + i];
                _tail = (_tail + 1) % _ringBuf.Length;
                _count++;
            }
            _lastDataTime = DateTime.UtcNow;
        }

        ProcessBuffer();
    }

    public void Write(byte[] data) => Write(data, 0, data.Length);

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
            byte[]? frame = null;

            lock (_lock)
            {
                if (_count == 0) return;
                frame = TryExtractFrame();
            }

            if (frame == null) return;
            EmitFrame(frame);
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
        for (int i = 0; i < count; i++)
        {
            result[i] = _ringBuf[_head];
            _head = (_head + 1) % _ringBuf.Length;
        }
        _count -= count;
        return result;
    }

    private void Advance(int count)
    {
        _head = (_head + count) % _ringBuf.Length;
        _count -= count;
    }

    private async void EmitFrame(byte[] frame)
    {
        var hex = HexHelper.BytesToHexSpaced(frame, 0, frame.Length);
        var text = Encoding.UTF8.GetString(frame);

        var entry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Direction = "RX",
            RawHex = hex,
            Text = text
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
                entry.Fields = await _parserManager.Engine.ExecuteAsync(frame, entry.Timestamp).ConfigureAwait(false);

            OnFrameAssembled?.Invoke(entry);
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"FrameBuffer error: {ex.Message}");
        }
    }

    private void CheckTimeout(object? state)
    {
        lock (_lock)
        {
            if (_count == 0) return;
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
    }
}
