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

        lock (_lock)
        {
            // Header check runs on the raw fragment (whitespace-skipping scan,
            // no string allocation); only after it passes does the fragment get
            // appended, matching the old Trim+StripSpaces+AppendHex control flow.
            if (_partialEntry == null && !MatchesHeader(entry.RawHex))
                return;

            var appended = AppendHexStripped(entry.RawHex);
            if (appended == 0)
                return;

            if (_partialEntry == null)
            {
                _partialEntry = entry;
                _lastReceiveTime = DateTime.UtcNow;
                TryComplete();
                return;
            }

            _lastReceiveTime = DateTime.UtcNow;
            TryComplete();
        }
    }

    /// <summary>Appends the hex characters of <paramref name="rawHex"/> into
    /// <paramref name="_hexBuf"/>, skipping whitespace, in one pass. Returns the
    /// number of characters appended (0 for an all-whitespace fragment).
    /// Replaces the former Trim + StripSpaces + AppendHex chain, which allocated
    /// two intermediate strings per fragment packet.</summary>
    private int AppendHexStripped(string rawHex)
    {
        int stripped = 0;
        foreach (var c in rawHex)
        {
            if (c is not ' ' and not '\t' and not '\r' and not '\n')
                stripped++;
        }
        if (stripped == 0)
            return 0;

        if (_hexLen + stripped > _hexBuf.Length)
            Array.Resize(ref _hexBuf, Math.Max(_hexLen + stripped, _hexBuf.Length * 2));

        int dst = _hexLen;
        foreach (var c in rawHex)
        {
            if (c is ' ' or '\t' or '\r' or '\n')
                continue;
            _hexBuf[dst++] = c;
        }
        _hexLen += stripped;
        return stripped;
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

    /// <summary>Checks whether the raw (space-separated) fragment starts with
    /// the configured header, skipping whitespace on the fly. Runs on the first
    /// fragment of a frame only, so the per-char scan is not a hot path; it
    /// avoids materializing a stripped copy just for the comparison.</summary>
    private bool MatchesHeader(string rawHex)
    {
        var headerNoSpace = GetHeaderNoSpace();
        if (headerNoSpace == null)
            return true;

        int hi = 0;
        foreach (var c in rawHex)
        {
            if (c is ' ' or '\t' or '\r' or '\n')
                continue;
            if (hi >= headerNoSpace.Length)
                return true; // header matched; remaining chars are payload
            if (char.ToUpperInvariant(c) != char.ToUpperInvariant(headerNoSpace[hi]))
                return false;
            hi++;
        }
        return hi == headerNoSpace.Length; // fragment exhausted exactly after header
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
