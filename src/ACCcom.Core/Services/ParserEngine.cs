using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class ParserEngine
{
    private Script<List<FieldAnnotation>>? _compiled;
    private string? _currentCode;
    private string? _lastError;

    public string? LastError => _lastError;

    public bool Load(string code)
    {
        try
        {
            var options = ScriptOptions.Default
                .WithImports("System", "System.Collections.Generic", "System.Linq", "ACCcom.Core.Models")
                .WithReferences(typeof(FieldAnnotation).Assembly);

            _compiled = CSharpScript.Create<List<FieldAnnotation>>(code, options, globalsType: typeof(ScriptGlobals));
            _compiled.Compile();
            _currentCode = code;
            _lastError = null;
            return true;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _compiled = null;
            return false;
        }
    }

    public List<FieldAnnotation>? Execute(byte[] data, DateTime timestamp)
    {
        if (_compiled == null) return null;

        try
        {
            var globals = new ScriptGlobals { RawData = data, Timestamp = timestamp };
            var result = _compiled.RunAsync(globals).GetAwaiter().GetResult();
            return result.ReturnValue;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            return null;
        }
    }

    public void Clear()
    {
        _compiled = null;
        _currentCode = null;
        _lastError = null;
    }
}
