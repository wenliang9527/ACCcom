using System.ComponentModel;
using ACCcom.Core.Services;
using ModelContextProtocol.Server;

namespace ACCcom.McpServer.Tools;

[McpServerToolType]
public class ParserTools
{
    private readonly ToolContext _ctx;
    private ProxyClient? _proxy => _ctx.Proxy;
    private ParserManager _parserManager => _ctx.ParserManager;

    public ParserTools(ToolContext ctx)
    {
        _ctx = ctx;
    }

    [McpServerTool, Description("List all available protocol parsers and the currently active one.")]
    public async Task<string> ListParsers()
    {
        if (_ctx.UseProxy) return await _proxy!.GetAsync("/api/parsers");
        return _ctx.RawJson(new { success = true, data = new { parsers = _parserManager.AvailableParsers.ToList(), active = _parserManager.ActiveParserName } });
    }

    [McpServerTool, Description("Read the source code of a .csx parser script. Parameters: name (parser name without .csx extension).")]
    public async Task<string> ReadParser([Description("Parser name (without .csx extension)")] string name)
    {
        if (_ctx.UseProxy) return await _proxy!.GetAsync($"/api/parser/read?name={Uri.EscapeDataString(name)}");
        var path = Path.Combine(_parserManager.GetParserDir(), name + ".csx");
        if (!File.Exists(path))
            return _ctx.RawJson(new { success = false, error = $"Parser '{name}' not found" });
        return _ctx.RawJson(new { success = true, data = new { name, code = File.ReadAllText(path) } });
    }

    [McpServerTool, Description("Write or update a .csx protocol parser script. The script uses ScriptGlobals helpers (RawHex, ToUInt16, ToFloat, Crc16, Sum8, Xor8). Must return List<FieldAnnotation>. Parameters: name (parser name), code (C# script code).")]
    public async Task<string> WriteParser(
        [Description("Parser name (without .csx extension)")] string name,
        [Description("C# script code that returns List<FieldAnnotation>")] string code)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(code))
            return _ctx.RawJson(new { success = false, error = "Both name and code are required" });
        if (_ctx.UseProxy) return await _proxy!.PostAsync("/api/parser/write", new { name, code });
        var path = Path.Combine(_parserManager.GetParserDir(), name + ".csx");
        File.WriteAllText(path, code);
        _parserManager.Refresh();
        return _ctx.RawJson(new { success = true, data = new { message = $"Parser '{name}' written", path } });
    }

    [McpServerTool, Description("Activate or deactivate a protocol parser. Pass null or empty to deactivate. Parameters: name (parser name to activate, null to deactivate).")]
    public async Task<string> ActivateParser([Description("Parser name to activate, or null to deactivate")] string? name)
    {
        if (_ctx.UseProxy) return await _proxy!.PostAsync("/api/parser/activate", new { name });
        _parserManager.Activate(string.IsNullOrEmpty(name) || name == ParserManager.NoParserName ? null : name);
        return _ctx.RawJson(new { success = true, data = new { message = _parserManager.ActiveParserName != null ? $"Parser '{_parserManager.ActiveParserName}' activated" : "Parser deactivated" } });
    }

    [McpServerTool, Description("Parse raw hex data offline using a protocol parser, without needing an open serial port. Parameters: hex (hex string like 'AA 55 03 ...'), parserName (optional, uses active parser if not specified).")]
    public async Task<string> ParseRaw(
        [Description("Hex data to parse (e.g. 'AA 55 03 01 19 2E')")] string hex,
        [Description("Parser name to use (null = use currently active parser)")] string? parserName = null)
    {
        if (string.IsNullOrEmpty(hex))
            return _ctx.RawJson(new { success = false, error = "Hex data is required" });
        if (_ctx.UseProxy) return await _proxy!.PostAsync("/api/parser/parse-raw", new { hex, parserName });
        byte[] data;
        try { data = Convert.FromHexString(hex.Replace(" ", "")); }
        catch { return _ctx.RawJson(new { success = false, error = "Invalid hex string" }); }

        var engine = _parserManager.Engine;
        if (!string.IsNullOrEmpty(parserName) && parserName != _parserManager.ActiveParserName)
        {
            var tempEngine = new ParserEngine();
            var path = Path.Combine(_parserManager.GetParserDir(), parserName + ".csx");
            if (!File.Exists(path)) return _ctx.RawJson(new { success = false, error = $"Parser '{parserName}' not found" });
            if (!tempEngine.Load(File.ReadAllText(path))) return _ctx.RawJson(new { success = false, error = $"Parser load failed: {tempEngine.LastError}" });
            engine = tempEngine;
        }
        if (_parserManager.ActiveParserName == null && string.IsNullOrEmpty(parserName))
            return _ctx.RawJson(new { success = false, error = "No active parser. Use activate_parser first or specify parserName." });

        var fields = engine.Execute(data, DateTime.Now);
        return _ctx.RawJson(new { success = true, data = new { hex, byteCount = data.Length, fields, fieldCount = fields?.Count ?? 0 } });
    }
}
