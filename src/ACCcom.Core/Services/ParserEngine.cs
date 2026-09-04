using System.Diagnostics;
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

    private readonly MemoryCache _cache;
    private readonly ReaderWriterLockSlim _rwLock = new();
    private readonly int _maxCacheSize;
    private string? _lastError;
    private string? _activeCode;
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
            if (_cache.TryGetValue(key, out _))
            {
                _activeCode = key;
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
            _activeCode = key;
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

    public async Task<List<FieldAnnotation>?> ExecuteAsync(byte[] data, DateTime timestamp, int timeoutMs = DefaultExecutionTimeoutMs)
    {
        Script<List<FieldAnnotation>>? script;

        _rwLock.EnterReadLock();
        try
        {
            if (_activeCode == null)
                return null;

            if (!_cache.TryGetValue(_activeCode, out var compiled))
                return null;

            script = compiled as Script<List<FieldAnnotation>>;
            if (script == null)
                return null;
        }
        finally
        {
            _rwLock.ExitReadLock();
        }

        using var cts = new CancellationTokenSource(timeoutMs);
        var sw = Stopwatch.StartNew();
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
            _metrics.RecordParseCompleted(true, sw.Elapsed.TotalMilliseconds);
            return result.ReturnValue;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _lastError = $"Script execution timed out after {timeoutMs}ms";
            OnError?.Invoke($"[ParserEngine] Execution timed out after {timeoutMs}ms");
            _metrics.RecordParseCompleted(false, sw.Elapsed.TotalMilliseconds);
            return null;
        }
        catch (OperationCanceledException)
        {
            _lastError = $"Script execution cancelled after {timeoutMs}ms";
            OnError?.Invoke($"[ParserEngine] Execution cancelled after {timeoutMs}ms");
            _metrics.RecordParseCompleted(false, sw.Elapsed.TotalMilliseconds);
            return null;
        }
        catch (CompilationErrorException ex)
        {
            _lastError = $"Compilation error: {ex.Message}";
            OnError?.Invoke($"[ParserEngine] Compilation error: {ex.Message}");
            _metrics.RecordParseCompleted(false, sw.Elapsed.TotalMilliseconds);
            return null;
        }
        catch (Exception ex)
        {
            _lastError = $"Execution failed: {ex.Message}";
            OnError?.Invoke($"[ParserEngine] Execution failed: {ex.Message}");
            _metrics.RecordParseCompleted(false, sw.Elapsed.TotalMilliseconds);
            return null;
        }
    }

    public void Clear()
    {
        _rwLock.EnterWriteLock();
        try
        {
            _cache.Compact(1.0);
            _activeCode = null;
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
