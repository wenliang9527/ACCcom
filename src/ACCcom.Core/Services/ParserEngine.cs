using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Caching.Memory;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class ParserEngine : IDisposable
{
    private static readonly ScriptOptions ScriptOptions = ScriptOptions.Default
        .WithImports("System", "System.Collections.Generic", "System.Linq", "ACCcom.Core.Models")
        .WithReferences(typeof(FieldAnnotation).Assembly);

    private const int DefaultExecutionTimeoutMs = 5000;

    static ParserEngine()
    {
        // Static Regex.Match(input, pattern) — the form every parser script
        // uses (esoac_v3.csx alone has 80+ distinct patterns) — routes through
        // a process-wide pattern cache that defaults to 15 entries. With far
        // more patterns than slots every text-log line re-parses the patterns
        // it misses (measured: 40 patterns x 50k lines ≈ 5.1s at the default
        // vs ≈0.29s at 256). One-time, process-wide; scripts are the dominant
        // user, so set it where scripts first run. HighlightService and
        // PatternMatcher keep their own caches and are unaffected.
        if (Regex.CacheSize < 256)
            Regex.CacheSize = 256;
    }

    private readonly MemoryCache _cache;
    private readonly ReaderWriterLockSlim _rwLock = new();
    private readonly int _maxCacheSize;
    private string? _lastError;
    // Direct reference to the active compiled script. ExecuteAsync is the
    // per-frame hot path; going through MemoryCache.TryGetValue (hash + size
    // accounting) plus a ReaderWriterLockSlim read every frame is pure overhead
    // for a value that only changes inside Load/Clear. Reference reads are
    // atomic, so ExecuteAsync needs no lock at all.
    private volatile Script<List<FieldAnnotation>>? _activeScript;
    private readonly MetricsCollector _metrics = MetricsCollector.Instance;

    public event Action<string>? OnError;

    public ParserEngine(int maxCacheSize = 10)
    {
        _maxCacheSize = maxCacheSize;
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = maxCacheSize
        });
    }

    public int MaxCacheSize => _maxCacheSize;

    public string? LastError => _lastError;

    public bool Load(string code)
    {
        var key = code;
        _rwLock.EnterWriteLock();
        try
        {
            if (_cache.TryGetValue(key, out var cached) && cached is Script<List<FieldAnnotation>> cachedScript)
            {
                _activeScript = cachedScript;
                return true;
            }

            var compiled = CSharpScript.Create<List<FieldAnnotation>>(code, ScriptOptions, globalsType: typeof(ScriptGlobals));
            var diagnostics = compiled.Compile();
            if (diagnostics.Any(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
            {
                _lastError = string.Join("\n", diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Select(d => d.GetMessage()));
                return false;
            }

            var options = new MemoryCacheEntryOptions()
                .SetSize(1)
                .SetSlidingExpiration(TimeSpan.FromMinutes(30))
                .SetPriority(CacheItemPriority.Normal);

            _cache.Set(key, compiled, options);
            _activeScript = compiled;
            _lastError = null;
            return true;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            return false;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    public async Task<List<FieldAnnotation>?> ExecuteAsync(byte[]? data, DateTime timestamp, int timeoutMs = DefaultExecutionTimeoutMs)
    {
        // Null input has nothing to parse; the script would only NRE on it.
        if (data == null) return null;

        // Volatile read of a field written under the write lock by Load/Clear.
        // No ReaderWriterLockSlim, no cache lookup: this runs per frame.
        var script = _activeScript;
        if (script == null) return null;

        // CancellationTokenSource throws for non-positive due-times; clamp so a
        // non-positive timeout behaves as "immediate timeout" instead of
        // surfacing an ArgumentOutOfRangeException to the caller.
        var effectiveTimeout = Math.Max(1, timeoutMs);
        using var cts = new CancellationTokenSource(effectiveTimeout);
        // GetTimestamp avoids the Stopwatch object allocation StartNew makes on
        // every frame; GetElapsedTime reads the same monotonic clock.
        var startTimestamp = Stopwatch.GetTimestamp();
        double ElapsedMs() => Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        try
        {
            var globals = new ScriptGlobals { RawData = data, Timestamp = timestamp };
            var task = script.RunAsync(globals, cts.Token);

            // WaitAsync registers a cancellation callback on the existing CTS
            // timer (created above with timeoutMs). The old Task.WhenAny + a
            // second Task.Delay allocated a fresh timer per execution that
            // stayed alive for the full timeout even when the script finished
            // instantly — at parser frame rates that stacked up live timers.
            var result = await task.WaitAsync(cts.Token).ConfigureAwait(false);
            _metrics.RecordParseCompleted(true, ElapsedMs());
            return result.ReturnValue;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _lastError = $"Script execution timed out after {timeoutMs}ms";
            OnError?.Invoke($"[ParserEngine] Execution timed out after {timeoutMs}ms");
            _metrics.RecordParseCompleted(false, ElapsedMs());
            return null;
        }
        catch (OperationCanceledException)
        {
            _lastError = $"Script execution cancelled after {timeoutMs}ms";
            OnError?.Invoke($"[ParserEngine] Execution cancelled after {timeoutMs}ms");
            _metrics.RecordParseCompleted(false, ElapsedMs());
            return null;
        }
        catch (CompilationErrorException ex)
        {
            _lastError = $"Compilation error: {ex.Message}";
            OnError?.Invoke($"[ParserEngine] Compilation error: {ex.Message}");
            _metrics.RecordParseCompleted(false, ElapsedMs());
            return null;
        }
        catch (Exception ex)
        {
            _lastError = $"Execution failed: {ex.Message}";
            OnError?.Invoke($"[ParserEngine] Execution failed: {ex.Message}");
            _metrics.RecordParseCompleted(false, ElapsedMs());
            return null;
        }
    }

    public void Clear()
    {
        _rwLock.EnterWriteLock();
        try
        {
            _cache.Compact(1.0);
            _activeScript = null;
            _lastError = null;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        _cache.Dispose();
        _rwLock.Dispose();
    }
}
