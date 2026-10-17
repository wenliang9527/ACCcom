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
    // Diagnostics: how many executions had to arm a deadline (i.e. the
    // submission suspended). A synchronously completing script must not appear
    // here — see ExecuteAsync.
    private long _timeoutSourcesCreated;

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

    /// <summary>
    /// Number of executions that armed a deadline. A submission that completes
    /// synchronously inside <c>RunAsync</c> cannot time out, so it must not
    /// create a timeout source (and its timer). Exposed for diagnostics/tests.
    /// </summary>
    internal long TimeoutSourcesCreated => Volatile.Read(ref _timeoutSourcesCreated);

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

        // GetTimestamp avoids the Stopwatch object allocation StartNew makes on
        // every frame; GetElapsedTime reads the same monotonic clock. Elapsed
        // time is measured inline rather than through a local function so the
        // per-frame path allocates no closure.
        var startTimestamp = Stopwatch.GetTimestamp();

        // The timeout source is created lazily, only for a submission that has
        // not already finished. new CancellationTokenSource(ms) arms a
        // TimerQueueTimer, and a script without awaits completes synchronously
        // inside RunAsync — so the eager version created and tore down a kernel
        // timer per parsed frame (~1 kHz in the MCP/RX path) to enforce a
        // timeout that could no longer fire.
        CancellationTokenSource? timeoutCts = null;
        try
        {
            var globals = new ScriptGlobals { RawData = data, Timestamp = timestamp };
            // CancellationToken.None: a submission that completes synchronously
            // cannot observe cancellation anyway, and the deadline is enforced
            // by WaitAsync below for one that suspends.
            var task = script.RunAsync(globals, CancellationToken.None);
            ScriptState<List<FieldAnnotation>> result;
            if (task.IsCompleted)
            {
                result = task.GetAwaiter().GetResult();
            }
            else
            {
                // CancellationTokenSource throws for non-positive due-times;
                // clamp so a non-positive timeout behaves as "immediate
                // timeout" instead of surfacing ArgumentOutOfRangeException.
                timeoutCts = new CancellationTokenSource(Math.Max(1, timeoutMs));
                Interlocked.Increment(ref _timeoutSourcesCreated);
                // WaitAsync registers a cancellation callback on that timer.
                // The old Task.WhenAny + a second Task.Delay allocated a fresh
                // timer per execution that stayed alive for the full timeout
                // even when the script finished instantly.
                result = await task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            _metrics.RecordParseCompleted(true, Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            return result.ReturnValue;
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
        {
            _lastError = $"Script execution timed out after {timeoutMs}ms";
            OnError?.Invoke($"[ParserEngine] Execution timed out after {timeoutMs}ms");
            _metrics.RecordParseCompleted(false, Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            return null;
        }
        catch (OperationCanceledException)
        {
            _lastError = $"Script execution cancelled after {timeoutMs}ms";
            OnError?.Invoke($"[ParserEngine] Execution cancelled after {timeoutMs}ms");
            _metrics.RecordParseCompleted(false, Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            return null;
        }
        catch (CompilationErrorException ex)
        {
            _lastError = $"Compilation error: {ex.Message}";
            OnError?.Invoke($"[ParserEngine] Compilation error: {ex.Message}");
            _metrics.RecordParseCompleted(false, Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            return null;
        }
        catch (Exception ex)
        {
            _lastError = $"Execution failed: {ex.Message}";
            OnError?.Invoke($"[ParserEngine] Execution failed: {ex.Message}");
            _metrics.RecordParseCompleted(false, Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            return null;
        }
        finally
        {
            timeoutCts?.Dispose();
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
