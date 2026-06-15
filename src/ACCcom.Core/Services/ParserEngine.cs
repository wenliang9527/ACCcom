using System.Threading;
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

    private readonly MemoryCache _cache;
    private readonly ReaderWriterLockSlim _rwLock = new();
    private readonly int _maxCacheSize;
    private string? _lastError;
    private string? _activeCode;

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

    public async Task<List<FieldAnnotation>?> ExecuteAsync(byte[] data, DateTime timestamp)
    {
        _rwLock.EnterReadLock();
        try
        {
            if (_activeCode == null)
                return null;

            if (!_cache.TryGetValue(_activeCode, out var compiled))
                return null;

            var script = (Script<List<FieldAnnotation>>?)compiled;
            if (script == null)
                return null;

            var globals = new ScriptGlobals { RawData = data, Timestamp = timestamp };
            var result = await script.RunAsync(globals).ConfigureAwait(false);
            return result.ReturnValue;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            OnError?.Invoke(ex.Message);
            return null;
        }
        finally
        {
            _rwLock.ExitReadLock();
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
