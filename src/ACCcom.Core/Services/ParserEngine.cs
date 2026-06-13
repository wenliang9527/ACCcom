using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class ParserEngine
{
    private readonly Dictionary<string, Script<List<FieldAnnotation>>> _cache = new();
    private readonly LinkedList<string> _order = new();
    private const int MaxCacheSize = 10;
    private string? _lastError;

    public string? LastError => _lastError;

    public bool Load(string code)
    {
        var key = code;
        if (_cache.TryGetValue(key, out _))
            return true;

        try
        {
            var options = ScriptOptions.Default
                .WithImports("System", "System.Collections.Generic", "System.Linq", "ACCcom.Core.Models")
                .WithReferences(typeof(FieldAnnotation).Assembly);

            var compiled = CSharpScript.Create<List<FieldAnnotation>>(code, options, globalsType: typeof(ScriptGlobals));
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
            _lastError = null;
            return true;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            return false;
        }
    }

    public async Task<List<FieldAnnotation>?> ExecuteAsync(byte[] data, DateTime timestamp)
    {
        if (_cache.Count == 0) return null;
        var compiled = _order.Last != null ? _cache[_order.Last!.Value] : null;
        if (compiled == null) return null;

        try
        {
            var globals = new ScriptGlobals { RawData = data, Timestamp = timestamp };
            var result = await compiled.RunAsync(globals).ConfigureAwait(false);
            return result.ReturnValue;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            return null;
        }
    }

    public List<FieldAnnotation>? Execute(byte[] data, DateTime timestamp)
    {
        if (_cache.Count == 0) return null;
        var compiled = _order.Last != null ? _cache[_order.Last!.Value] : null;
        if (compiled == null) return null;

        try
        {
            var globals = new ScriptGlobals { RawData = data, Timestamp = timestamp };
            var task = compiled.RunAsync(globals);
            if (task.IsCompleted)
                return task.Result.ReturnValue;
            return task.ConfigureAwait(false).GetAwaiter().GetResult().ReturnValue;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            return null;
        }
    }

    public void Clear()
    {
        _cache.Clear();
        _order.Clear();
        _lastError = null;
    }
}