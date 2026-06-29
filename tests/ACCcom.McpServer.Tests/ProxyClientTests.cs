using System.Text.Json;
using ACCcom.McpServer;

namespace ACCcom.McpServer.Tests;

/// <summary>
/// ProxyClient 错误处理测试:验证网络错误/超时/非2xx 都能返回带诊断信息的 JSON。
/// </summary>
public class ProxyClientTests
{
    private static string UnusedProxyPort => $"http://127.0.0.1:{10000 + Random.Shared.Next(0, 5000)}";

    [Fact]
    public async Task GetAsync_WithUnreachableHost_ReturnsConnectionError()
    {
        // 指向一个几乎肯定没人监听的端口
        using var client = new ProxyClient(UnusedProxyPort);
        var json = await client.GetAsync("/api/health");

        Assert.False(ExtractBool(json, "success"));
        var kind = ExtractString(json, "errorKind");
        Assert.True(kind == "connection" || kind == "timeout",
            $"expected connection/timeout, got {kind}");
        Assert.Contains("hint", json);
    }

    [Fact]
    public async Task PostAsync_WithUnreachableHost_ReturnsConnectionError()
    {
        using var client = new ProxyClient(UnusedProxyPort);
        var json = await client.PostAsync("/api/send", new { data = "test" });

        Assert.False(ExtractBool(json, "success"));
        Assert.NotNull(ExtractString(json, "errorKind"));
        Assert.NotNull(ExtractString(json, "method"));
        Assert.NotNull(ExtractString(json, "path"));
    }

    [Fact]
    public async Task GetAsync_ErrorContainsMethodAndPath()
    {
        using var client = new ProxyClient(UnusedProxyPort);
        var json = await client.GetAsync("/api/status");

        Assert.Contains("\"method\":\"GET\"", json);
        Assert.Contains("\"path\":\"/api/status\"", json);
    }

    [Fact]
    public async Task PostAsync_ErrorContainsMethodAndPath()
    {
        using var client = new ProxyClient(UnusedProxyPort);
        var json = await client.PostAsync("/api/port/open", new { port = "COM9" });

        Assert.Contains("\"method\":\"POST\"", json);
        Assert.Contains("\"path\":\"/api/port/open\"", json);
    }

    [Fact]
    public void Constructor_SetsTimeout()
    {
        using var client = new ProxyClient("http://127.0.0.1:8899");
        Assert.Equal(ProxyClient.DefaultTimeoutSec, 65);
    }

    private static bool ExtractBool(string json, string prop)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.False ? false : v.GetBoolean();
        }
        catch { return false; }
    }

    private static string? ExtractString(string json, string prop)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
        catch { return null; }
    }
}
