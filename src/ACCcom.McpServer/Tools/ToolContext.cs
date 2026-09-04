using System.Text.Json;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.McpServer.Tools;

/// <summary>
/// Shared context for all MCP tool classes.
/// Holds the serial service, shared buffer, and common helpers.
/// </summary>
public class ToolContext
{
    public ISerialService Serial { get; }
    public DataBufferService Buffer { get; } = new();

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public ToolContext(ISerialService serial)
    {
        Serial = serial;
        Serial.OnDataReceived += entry => Buffer.AddEntry(entry);
    }

    public string RawJson(object obj) =>
        JsonSerializer.Serialize(obj, JsonOpts);
}
