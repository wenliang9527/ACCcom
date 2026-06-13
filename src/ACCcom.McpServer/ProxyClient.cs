using System.Net.Http.Json;
using System.Text.Json;

namespace ACCcom.McpServer;

/// <summary>
/// HTTP proxy client for --proxy mode.
/// Forwards MCP tool calls to the ACCCOM WPF HTTP API.
/// </summary>
public class ProxyClient
{
    private readonly HttpClient _http;

    public ProxyClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
    }

    public async Task<string> GetAsync(string path)
    {
        try
        {
            var resp = await _http.GetAsync(path.TrimStart('/'));
            return await resp.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = $"Proxy request failed: {ex.Message}. Is ACCCOM WPF running?" });
        }
    }

    public async Task<string> PostAsync(string path, object? body = null)
    {
        try
        {
            HttpContent? content = null;
            if (body != null) content = JsonContent.Create(body);
            var resp = await _http.PostAsync(path.TrimStart('/'), content);
            return await resp.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = $"Proxy request failed: {ex.Message}. Is ACCCOM WPF running?" });
        }
    }
}
