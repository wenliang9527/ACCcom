using System.Text;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class FrameAssembler : IDisposable
{
    private readonly FrameAssemblerConfig _config;
    private readonly ParserManager? _parserManager;
    private readonly object _lock = new();
    private LogEntry? _partialEntry;
    // Hex fragments accumulate into a growable char[] instead of a string +=
    // per fragment: multi-fragment frames were O(n²) on the concatenation and
    // allocated an intermediate string per Feed.
    private char[] _hexBuf = new char[256];
    private int _hexLen;
    private string? _cachedHeaderSource;
    private string? _cachedHeaderNoSpace;
    private DateTime _lastReceiveTime;
    private Timer? _timer;
    private bool _disposed;

    public bool IsEnabled => _config.Enabled;
    public FrameAssemblerConfig Config => _config;

    public event Action<LogEntry>? OnFrameAssembled;
    public event Action<string>? OnError;

    public FrameAssembler(FrameAssemblerConfig config, ParserManager? parserManager = null)
    {
        _config = config;
        _parserManager = parserManager;
        _timer = new Timer(CheckTimeout, null, 200, 200);
    }

    public void Feed(LogEntry entry)
    {
        if (_disposed || !_config.Enabled)
            return;

        if (string.IsNullOrEmpty(entry.RawHex))
            return;

        var hex = entry.RawHex.Trim();
        if (string.IsNullOrEmpty(hex))
            return;

        lock (_lock)
        {
            var hexNoSpace = StripSpaces(hex);

            if (_partialEntry == null)
            {
                if (!MatchesHeader(hexNoSpace))
                    return;

                AppendHex(hexNoSpace);
                _partialEntry = entry;
                _lastReceiveTime = DateTime.UtcNow;
                TryComplete();
                return;
            }

            AppendHex(hexNoSpace);
            _lastReceiveTime = DateTime.UtcNow;
            TryComplete();
        }
    }

    private void AppendHex(string hexNoSpace)
    {
        if (_hexLen + hexNoSpace.Length > _hexBuf.Length)
            Array.Resize(ref _hexBuf, Math.Max(_hexLen + hexNoSpace.Length, _hexBuf.Length * 2));
        hexNoSpace.CopyTo(0, _hexBuf, _hexLen, hexNoSpace.Length);
        _hexLen += hexNoSpace.Length;
    }

    private void TryComplete()
    {
        if (_partialEntry == null || _hexLen == 0)
            return;

        var hexLen = _hexLen / 2;

        if (_config.LengthFieldOffset >= 0 && hexLen > _config.LengthFieldOffset + _config.LengthFieldSize)
        {
            var bytes = HexToBytes();
            var frameLen = ReadLengthField(bytes, _config.LengthFieldOffset, _config.LengthFieldSize);

            if (hexLen >= frameLen)
            {
                _ = EmitCompleteAsync(bytes);
                return;
            }

            if (hexLen > _config.MaxFrameSize)
            {
                Reset();
            }
        }
        else if (_config.LengthFieldOffset < 0)
        {
            var bytes = HexToBytes();
            _ = EmitCompleteAsync(bytes);
        }
    }

    private async Task EmitCompleteAsync(byte[] bytes)
    {
        LogEntry? entry = null;

        lock (_lock)
        {
            if (_partialEntry == null)
                return;

            var text = Encoding.UTF8.GetString(bytes);
            var hex = FormatHex(bytes);

            entry = new LogEntry
            {
                Id = _partialEntry.Id,
                Timestamp = _partialEntry.Timestamp,
                Direction = _partialEntry.Direction,
                PortTag = _partialEntry.PortTag,
                RawHex = hex,
                Text = text
            };

            Reset();
        }

        if (entry == null)
            return;

        try
        {
            if (_parserManager?.ActiveParserName != null)
            {
                entry.Fields = await _parserManager.Engine.ExecuteAsync(bytes, entry.Timestamp).ConfigureAwait(false);
            }

            OnFrameAssembled?.Invoke(entry);
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"FrameAssembler error: {ex.Message}");
        }
    }

    private bool MatchesHeader(string hexNoSpace)
    {
        var headerNoSpace = GetHeaderNoSpace();
        if (headerNoSpace == null)
            return true;

        if (hexNoSpace.Length < headerNoSpace.Length)
            return false;

        return hexNoSpace.StartsWith(headerNoSpace, StringComparison.OrdinalIgnoreCase);
    }

    // The header never changes during the assembler's lifetime; stripping it on
    // every Feed allocated a string per packet. Cache the stripped form, keyed
    // on the config instance so a swapped config object is picked up lazily.
    private string? GetHeaderNoSpace()
    {
        var header = _config.Header;
        if (!ReferenceEquals(_cachedHeaderSource, header))
        {
            _cachedHeaderSource = header;
            _cachedHeaderNoSpace = string.IsNullOrEmpty(header) ? null : StripSpaces(header);
        }
        return _cachedHeaderNoSpace;
    }

    private static int ReadLengthField(byte[] bytes, int offset, int size)
    {
        if (offset < 0 || offset + size > bytes.Length)
            return 0;

        if (size == 1)
            return bytes[offset];
        if (size == 2)
            return (bytes[offset] << 8) | bytes[offset + 1];

        return 0;
    }

    public void Reset()
    {
        lock (_lock)
        {
            _hexLen = 0;
            _partialEntry = null;
            _lastReceiveTime = DateTime.MinValue;
        }
    }

    private void CheckTimeout(object? state)
    {
        lock (_lock)
        {
            if (_partialEntry == null)
                return;

            if ((DateTime.UtcNow - _lastReceiveTime).TotalMilliseconds >= _config.PartialFrameTimeoutMs)
            {
                Reset();
            }
        }
    }

    private static string StripSpaces(string hex)
    {
        var sb = new StringBuilder(hex.Length);
        foreach (var c in hex.AsSpan())
        {
            if (c != ' ' && c != '\t' && c != '\r' && c != '\n')
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static string FormatHex(byte[] bytes)
    {
        return HexHelper.BytesToHexSpaced(bytes, 0, bytes.Length);
    }

    private byte[] HexToBytes()
    {
        return Convert.FromHexString(_hexBuf.AsSpan(0, _hexLen));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
    }
}
