namespace ACCcom.Core.Services;

/// <summary>
/// Bounded cache of compiled parser engines, keyed by parser name and
/// invalidated by the .csx file's last-write time. parse-raw with an explicit
/// parserName used to compile a fresh Roslyn script per request (hundreds of
/// milliseconds) and throw it away; hot reload still wins because a touched
/// file drops its entry. Evicted engines are disposed — a ParserEngine owns a
/// MemoryCache.
///
/// Extracted from HttpService rather than inlined there because HttpService
/// needs a running EmbedIO server to construct, which would make the cache
/// logic untestable in isolation.
/// </summary>
internal sealed class ParserEngineCache : IDisposable
{
    private const int DefaultCapacity = 8;

    private readonly int _capacity;
    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private long _seq;
    private bool _disposed;

    internal ParserEngineCache(int capacity = DefaultCapacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    private sealed record Entry(ParserEngine Engine, DateTime LoadedUtc, long Seq);

    /// <summary>Number of live cached engines. Test seam plus an operational
    /// signal for the cap actually doing something.</summary>
    internal int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    /// <summary>Returns the engine for <paramref name="name"/>, compiling it from
    /// <paramref name="path"/> on a miss or a stale mtime. Returns null and sets
    /// <paramref name="error"/> when the script does not compile or the file
    /// cannot be read.</summary>
    internal ParserEngine? GetOrLoad(string name, string path, out string? error)
    {
        error = null;
        var loadedUtc = SafeLastWriteUtc(path);

        lock (_lock)
        {
            if (_entries.TryGetValue(name, out var hit) && hit.LoadedUtc == loadedUtc)
                return hit.Engine;
        }

        ParserEngine fresh;
        try
        {
            fresh = new ParserEngine();
            if (!fresh.Load(File.ReadAllText(path)))
            {
                error = fresh.LastError;
                fresh.Dispose();
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // The file existed at resolve time and vanished or became
            // unreadable before the read — report it like a load failure so the
            // caller's error-return contract holds.
            error = ex.Message;
            return null;
        }

        lock (_lock)
        {
            // Another thread may have won the race; prefer the existing entry so
            // we do not leak a duplicate engine.
            if (_entries.TryGetValue(name, out var raced) && raced.LoadedUtc == loadedUtc)
            {
                fresh.Dispose();
                return raced.Engine;
            }

            _entries[name] = new Entry(fresh, loadedUtc, ++_seq);
            EvictOverflowLocked();
            return fresh;
        }
    }

    private void EvictOverflowLocked()
    {
        while (_entries.Count > _capacity)
        {
            var oldest = _entries.OrderBy(kv => kv.Value.Seq).First();
            _entries.Remove(oldest.Key);
            oldest.Value.Engine.Dispose();
        }
    }

    private static DateTime SafeLastWriteUtc(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var kv in _entries)
                kv.Value.Engine.Dispose();
            _entries.Clear();
        }
        GC.SuppressFinalize(this);
    }
}
