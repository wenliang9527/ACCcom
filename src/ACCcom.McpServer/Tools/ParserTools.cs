using System.ComponentModel;
using ACCcom.Core.Models;
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
    public Task<string> ListParsers()
    {
        if (_ctx.UseProxy) return _proxy!.GetAsync("/api/parsers");
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { parsers = _parserManager.AvailableParsers.ToList(), active = _parserManager.ActiveParserName } }));
    }

    [McpServerTool, Description("Read the source code of a .csx parser script. Parameters: name (parser name without .csx extension).")]
    public Task<string> ReadParser([Description("Parser name (without .csx extension)")] string name)
    {
        if (_ctx.UseProxy) return _proxy!.GetAsync($"/api/parser/read?name={Uri.EscapeDataString(name)}");
        var path = Path.Combine(_parserManager.GetParserDir(), name + ".csx");
        if (!File.Exists(path))
            return Task.FromResult(_ctx.RawJson(new { success = false, error = $"Parser '{name}' not found" }));
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { name, code = File.ReadAllText(path) } }));
    }

    [McpServerTool, Description("Write or update a .csx protocol parser script. The script uses ScriptGlobals helpers (RawHex, ToUInt16, ToFloat, Crc16, Sum8, Xor8). Must return List<FieldAnnotation>. Parameters: name (parser name), code (C# script code).")]
    public Task<string> WriteParser(
        [Description("Parser name (without .csx extension)")] string name,
        [Description("C# script code that returns List<FieldAnnotation>")] string code)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(code))
            return Task.FromResult(_ctx.RawJson(new { success = false, error = "Both name and code are required" }));
        if (_ctx.UseProxy) return _proxy!.PostAsync("/api/parser/write", new { name, code });
        var path = Path.Combine(_parserManager.GetParserDir(), name + ".csx");
        File.WriteAllText(path, code);
        _parserManager.Refresh();
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { message = $"Parser '{name}' written", path } }));
    }

    [McpServerTool, Description("Activate or deactivate a protocol parser. Pass null or empty to deactivate. Parameters: name (parser name to activate, null to deactivate).")]
    public Task<string> ActivateParser([Description("Parser name to activate, or null to deactivate")] string? name)
    {
        if (_ctx.UseProxy) return _proxy!.PostAsync("/api/parser/activate", new { name });
        _parserManager.Activate(string.IsNullOrEmpty(name) || name == ParserManager.NoParserName ? null : name);
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { message = _parserManager.ActiveParserName != null ? $"Parser '{_parserManager.ActiveParserName}' activated" : "Parser deactivated" } }));
    }

    [McpServerTool, Description("Parse raw hex data offline using a protocol parser, without needing an open serial port. Parameters: hex (hex string like 'AA 55 03 ...'), parserName (optional, uses active parser if not specified).")]
    public async Task<string> ParseRaw(
        [Description("Hex data to parse (e.g. 'AA 55 03 01 19 2E')")] string hex,
        [Description("Parser name to use (null = use currently active parser)")] string? parserName = null)
    {
        if (string.IsNullOrEmpty(hex))
            return _ctx.RawJson(new { success = false, error = "Hex data is required" });
        if (_ctx.UseProxy) return await _proxy!.PostAsync("/api/parser/parse-raw", new { hex, parserName }).ConfigureAwait(false);
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

        var fields = await engine.ExecuteAsync(data, DateTime.Now).ConfigureAwait(false);
        return _ctx.RawJson(new { success = true, data = new { hex, byteCount = data.Length, fields, fieldCount = fields?.Count ?? 0 } });
    }

    [McpServerTool, Description("Generate a .csx parser from a protocol schema JSON. The JSON describes frame structure, fields, commands, and checksum. Returns the generated parser code.")]
    public Task<string> GenerateParser(
        [Description("Protocol schema JSON string")] string schemaJson,
        [Description("Parser name (without .csx extension)")] string name)
    {
        if (string.IsNullOrEmpty(schemaJson) || string.IsNullOrEmpty(name))
            return Task.FromResult(_ctx.RawJson(new { success = false, error = "Both schemaJson and name are required" }));

        var generator = new ParserGenerator();
        var schema = generator.ParseJson(schemaJson);
        if (schema == null)
            return Task.FromResult(_ctx.RawJson(new { success = false, error = "Invalid JSON schema" }));

        schema.Name = name;

        if (_ctx.UseProxy)
            return _proxy!.PostAsync("/api/parser/generate", new { schemaJson, name });

        var (success, error) = _parserManager.GenerateParser(schema);
        if (!success)
            return Task.FromResult(_ctx.RawJson(new { success = false, error }));

        var path = Path.Combine(_parserManager.GetParserDir(), name + ".csx");
        var code = File.ReadAllText(path);
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { message = $"Parser '{name}' generated", path, code } }));
    }

    [McpServerTool, Description("Validate a protocol schema JSON without generating. Returns validation errors if any.")]
    public Task<string> ValidateSchema(
        [Description("Protocol schema JSON string")] string schemaJson)
    {
        var generator = new ParserGenerator();
        var schema = generator.ParseJson(schemaJson);
        if (schema == null)
            return Task.FromResult(_ctx.RawJson(new { success = false, error = "Invalid JSON" }));

        var (valid, errors) = generator.Validate(schema);
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { valid, errors } }));
    }

    [McpServerTool, Description("Get a template protocol schema JSON for reference.")]
    public Task<string> GetSchemaTemplate()
    {
        var template = new ProtocolSchema
        {
            Name = "my_protocol",
            Description = "My custom protocol",
            Type = "binary",
            MinLength = 6,
            Frame = new FrameSchema
            {
                Header = "AA 55",
                Footer = "FF",
                LengthField = new LengthFieldSchema { Offset = 2, Length = 1 },
                Checksum = new ChecksumSchema { Type = "crc16", Algorithm = "modbus" },
                CommandField = new CommandFieldSchema { Offset = 3, Length = 1 }
            },
            Fields = new List<FieldSchema>
            {
                new FieldSchema { Name = "帧头", Offset = 0, Length = 2, Type = "hex", Value = "AA 55" },
                new FieldSchema { Name = "长度", Offset = 2, Length = 1, Type = "uint8" },
                new FieldSchema { Name = "命令", Offset = 3, Length = 1, Type = "enum", Values = new Dictionary<string, string> { { "0x01", "查询" }, { "0x02", "设置" } } },
                new FieldSchema { Name = "数据", Offset = 4, Length = 1, Type = "uint8" }
            },
            Commands = new Dictionary<string, CommandSchema>
            {
                ["0x01"] = new CommandSchema
                {
                    Name = "查询命令",
                    Fields = new List<FieldSchema>
                    {
                        new FieldSchema { Name = "查询类型", Offset = 4, Length = 1, Type = "enum", Values = new Dictionary<string, string> { { "0x01", "温度" }, { "0x02", "状态" } } }
                    }
                },
                ["0x02"] = new CommandSchema
                {
                    Name = "设置命令",
                    Fields = new List<FieldSchema>
                    {
                        new FieldSchema { Name = "设置值", Offset = 4, Length = 1, Type = "uint8", Unit = "°C" }
                    }
                }
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(template, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { template = json } }));
    }
}
