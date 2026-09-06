namespace ACCcom.Core.Services;

/// <summary>
/// Bounded, lock-protected buffer of recent RX text strings consumed by the
/// macro engine's WaitFor/Condition polling (background task) while RX entries
/// keep arriving on the receive thread. Trimming is chunked: RemoveRange from
/// index 0 shifts every remaining element, so trimming on every RX frame would
/// memmove the whole cap per packet — instead we trim only once the backlog
/// grows a full chunk past the cap, keeping the list between cap and
/// cap+chunk entries. Extracted from DataFlowViewModel's recent-RX list so the
/// chunking and thread-safety semantics are unit-testable.
/// </summary>
public class RecentRxTextBuffer
{
    private readonly int _cap;
    private readonly int _trimChunk;
    private readonly object _lock = new();
    private readonly List<string> _texts = new();

    /// <summary>Creates a buffer. <paramref name="cap"/> is clamped to at least
    /// 1 and <paramref name="trimChunk"/> to at least 1 so a misconfigured
    /// caller cannot disable trimming entirely.</summary>
    public RecentRxTextBuffer(int cap, int trimChunk)
    {
        _cap = Math.Max(1, cap);
        _trimChunk = Math.Max(1, trimChunk);
    }

    /// <summary>Number of buffered texts (for observability/tests).</summary>
    public int Count
    {
        get { lock (_lock) return _texts.Count; }
    }

    /// <summary>Appends a text and trims a whole chunk only when the backlog
    /// exceeds cap + chunk. Empty/whitespace texts are ignored.</summary>
    public void Add(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (_lock)
        {
            _texts.Add(text);
            if (_texts.Count > _cap + _trimChunk)
                _texts.RemoveRange(0, _texts.Count - _cap);
        }
    }

    /// <summary>Clears all buffered texts; call before starting a macro run so
    /// waits only see data received during that run.</summary>
    public void Clear()
    {
        lock (_lock)
            _texts.Clear();
    }

    /// <summary>Returns the most recent text containing
    /// <paramref name="pattern"/> (case-insensitive), or null. Scans newest
    /// first, mirroring the consumer's "most recent match wins" semantics.</summary>
    public string? FindLatestContaining(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return null;
        lock (_lock)
        {
            for (int i = _texts.Count - 1; i >= 0; i--)
            {
                if (_texts[i].Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return _texts[i];
            }
        }
        return null;
    }
}
