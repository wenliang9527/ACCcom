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
        var services = new ServiceCollection();
        services.AddSingleton<ISerialService, VirtualSerialService>();
        services.AddSingleton<ToolContext>();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<ToolContext>(), sp);
    }

    public static bool ExtractSuccess(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean();
        }
        catch { return false; }
    }

    public static string? ExtractError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        }
        catch { return null; }
    }
}
