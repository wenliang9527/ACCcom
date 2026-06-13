using System.Threading;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class ParserEngine
{
    private static readonly ScriptOptions ScriptOptions = ScriptOptions.Default
        .WithImports("System", "System.Collections.Generic", "System.Linq", "ACCcom.Core.Models")
        .WithReferences(typeof(FieldAnnotation).Assembly);

    private readonly Dictionary<string, Script<List<FieldAnnotation>>> _cache = new();
    private readonly LinkedList<string> _order = new();
    private readonly ReaderWriterLockSlim _rwLock = new();
    private const int MaxCacheSize = 10;
    private string? _lastError;
    private string? _activeCode;

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

            if (_cache.Count >= MaxCacheSize)
            {
                var oldest = _order.First!.Value;
                _order.RemoveFirst();
                _cache.Remove(oldest);
            }

            _cache[key] = compiled;
            _order.AddLast(key);
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
            if (_activeCode == null || !_cache.TryGetValue(_activeCode, out var compiled))
                return null;

            var globals = new ScriptGlobals { RawData = data, Timestamp = timestamp };
            var result = await compiled.RunAsync(globals).ConfigureAwait(false);
            return result.ReturnValue;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
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
            _cache.Clear();
            _order.Clear();
            _activeCode = null;
            _lastError = null;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }
}