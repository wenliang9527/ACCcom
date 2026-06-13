using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.McpServer.Tools;

/// <summary>
/// Shared context for all MCP tool classes.
/// Holds service references, shared buffer, and common helpers.
/// </summary>
public class ToolContext
{
    public ISerialService? Serial { get; }
    public ProxyClient? Proxy { get; }
    public ParserManager ParserManager { get; }
    public LoggerService? Logger { get; }
    public bool UseProxy { get; }
    public DataBufferService Buffer { get; } = new();
    public DataStatistics? Stats { get; }
    public MultiPortService? MultiPort { get; }
    public AutoBaudDetector? AutoBaud { get; }
    public SessionRecorder Recorder { get; }

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public ToolContext(IServiceProvider sp, ParserManager parserManager)
    {
        ParserManager = parserManager;
        Proxy = sp.GetService<ProxyClient>();
        UseProxy = Proxy != null;
        Recorder = sp.GetRequiredService<SessionRecorder>();

        if (!UseProxy)
        {
            Serial = sp.GetRequiredService<ISerialService>();
            Logger = sp.GetRequiredService<LoggerService>();
            Stats = new DataStatistics();
            MultiPort = sp.GetService<MultiPortService>();
            AutoBaud = sp.GetService<AutoBaudDetector>();

            Serial.OnDataReceived += entry =>
            {
                Buffer.AddEntry(entry);
                Logger.Write(entry);
                Recorder.Record(entry);
                if (entry.Direction == "RX")
                {
                    var bytes = string.IsNullOrEmpty(entry.RawHex) ? 0 : entry.RawHex.Replace(" ", "").Length / 2;
                    Stats.RecordRx(bytes);
                }
            };
        }
    }

    public (ParserEngine engine, string? error) GetParserEngine(string? parserName)
    {
        if (string.IsNullOrEmpty(parserName) || parserName == ParserManager.ActiveParserName)
            return (ParserManager.Engine, null);

        var path = Path.Combine(ParserManager.GetParserDir(), parserName + ".csx");
        if (!File.Exists(path))
            return (ParserManager.Engine, $"Parser '{parserName}' not found");

        var engine = new ParserEngine();
        if (!engine.Load(File.ReadAllText(path)))
            return (ParserManager.Engine, $"Parser load failed: {engine.LastError}");

        return (engine, null);
    }

    public string RawJson(object obj) =>
        JsonSerializer.Serialize(obj, JsonOpts);
}
