using System.Text.Json;
using ACCcom.Core.Services;
using ACCcom.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace ACCcom.McpServer.Tests.TestHelpers;

/// <summary>
/// 构建 ToolContext 的测试辅助类,注入 VirtualSerialService 避免真实串口依赖。
/// </summary>
internal static class ToolContextFactory
{
    public static (ToolContext ctx, ServiceProvider sp) Create()
    {
        // Swap the shared traffic log for a temp-file instance: traffic
        // recorded by tests must not touch the real mcp-traffic.jsonl, and
        // the instance is registered in the container so sp.Dispose() flushes
        // and deletes it. Registered before Build so the provider owns it.
        var tempPath = Path.Combine(Path.GetTempPath(),
            $"acccom-test-traffic-{Guid.NewGuid():N}.jsonl");

        var services = new ServiceCollection();
        services.AddSingleton<ISerialService, VirtualSerialService>();
        // Multi-port service with the same virtual factory; the default
        // single-port session is the ToolContext's Serial.
        services.AddSingleton(new MultiPortService(() => new VirtualSerialService()));
        services.AddSingleton(_ => new McpTrafficLog(tempPath));
        services.AddSingleton<ToolContext>();

        var sp = services.BuildServiceProvider();
        var ctx = sp.GetRequiredService<ToolContext>();
        ctx.TrafficLog = sp.GetRequiredService<McpTrafficLog>();
        return (ctx, sp);
    }

    /// <summary>The temp JSONL path backing a factory-created context's
    /// TrafficLog — lets tests read recorded lines after the provider
    /// disposes (which flushes buffered lines to disk).</summary>
    public static string TrafficLogPath(ServiceProvider sp)
        => sp.GetRequiredService<McpTrafficLog>().FilePath;

    public static bool ExtractSuccess(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean();
        }
        catch { return false; }
    }

    /// <summary>Extracts the human-readable message from the standard failure
    /// envelope {"success":false,"error":{"code","message"}}. Tolerates the
    /// legacy bare-string form so a contract drift shows up as a null (failed
    /// assertion) instead of an exception.</summary>
    public static string? ExtractError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("error", out var e)) return null;
            return e.ValueKind == JsonValueKind.String
                ? e.GetString()
                : e.GetProperty("message").GetString();
        }
        catch { return null; }
    }

    /// <summary>Extracts the stable error code (INVALID_HEX, PORT_NOT_OPEN, …)
    /// from the failure envelope; null when absent or malformed.</summary>
    public static string? ExtractErrorCode(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("error", out var e)) return null;
            return e.ValueKind == JsonValueKind.Object ? e.GetProperty("code").GetString() : null;
        }
        catch { return null; }
    }
}
