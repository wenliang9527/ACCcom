using System.Text.Json;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using ACCcom.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace ACCcom.McpServer.Tests.TestHelpers;

/// <summary>
/// 构建 ToolContext 的测试辅助类,支持直接模式(默认)与代理模式。
/// 直接模式注入 VirtualSerialService 避免真实串口依赖。
/// </summary>
internal static class ToolContextFactory
{
    public static (ToolContext ctx, ServiceProvider sp) CreateDirect(string? parserDir = null)
    {
        parserDir ??= Path.Combine(AppContext.BaseDirectory, "parsers");

        // 使用临时目录避免 LocalApplicationData 权限问题
        var tempRoot = Path.Combine(Path.GetTempPath(), "ACCcom_McpTests_" + Guid.NewGuid().ToString("N")[..8]);
        var logDir = Path.Combine(tempRoot, "logs");
        Directory.CreateDirectory(logDir);

        var services = new ServiceCollection();
        services.AddSingleton<ISerialService, VirtualSerialService>();
        services.AddSingleton(_ => new ParserManager(parserDir));
        services.AddSingleton(_ => new LoggerService(logDir));
        services.AddSingleton<MultiPortService>();
        services.AddSingleton<AutoBaudDetector>();
        services.AddSingleton<ModbusConnectionManager>();
        services.AddSingleton(sp =>
        {
            var serial = sp.GetRequiredService<ISerialService>();
            var mgr = sp.GetRequiredService<ModbusConnectionManager>();
            return mgr.GetDefaultService(serial);
        });
        services.AddSingleton<ModbusSlaveService>();
        services.AddSingleton<SessionRecorder>();
        services.AddSingleton<ToolContext>();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<ToolContext>(), sp);
    }

    public static (ToolContext ctx, ServiceProvider sp) CreateProxy(string proxyUrl)
    {
        var parserDir = Path.Combine(AppContext.BaseDirectory, "parsers");

        var services = new ServiceCollection();
        services.AddSingleton(_ => new ProxyClient(proxyUrl));
        services.AddSingleton(_ => new ParserManager(parserDir));
        services.AddSingleton<SessionRecorder>();
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
