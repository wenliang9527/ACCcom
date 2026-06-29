using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

namespace ACCcom.McpServer;

/// <summary>
/// HTTP proxy client for --proxy mode.
/// Forwards MCP tool calls to the ACCCOM WPF HTTP API.
/// 区分网络异常、超时、非 2xx 状态码,返回带诊断信息的 JSON。
/// </summary>
public class ProxyClient : IDisposable
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>默认单次请求超时(秒)。略大于 Modbus 等长操作上限(60s),避免误杀。</summary>
    public const int DefaultTimeoutSec = 65;

    public ProxyClient(string baseUrl)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(DefaultTimeoutSec)
        };
    }

    public void Dispose() => _http.Dispose();

    public async Task<string> GetAsync(string path)
    {
        try
        {
            using var resp = await _http.GetAsync(path.TrimStart('/')).ConfigureAwait(false);
            return await ReadResponseAsync(resp, "GET", path).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return BuildErrorJson(ex, "GET", path);
        }
    }

    public async Task<string> PostAsync(string path, object? body = null)
    {
        try
        {
            HttpContent? content = body != null ? JsonContent.Create(body) : null;
            using var resp = await _http.PostAsync(path.TrimStart('/'), content).ConfigureAwait(false);
            return await ReadResponseAsync(resp, "POST", path).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return BuildErrorJson(ex, "POST", path);
        }
    }

    private static async Task<string> ReadResponseAsync(HttpResponseMessage resp, string method, string path)
    {
        // 非 2xx 视为业务层失败,但仍尽量透传 WPF 端返回的 JSON 错误信息
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var reason = string.IsNullOrWhiteSpace(body) ? resp.ReasonPhrase ?? resp.StatusCode.ToString() : body;
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = $"WPF HTTP {method} {path} returned {(int)resp.StatusCode} {resp.StatusCode}: {reason}",
                statusCode = (int)resp.StatusCode,
                method,
                path
            }, JsonOpts);
        }

        // 2xx: 原样透传 WPF 端的 JSON 响应(保持 ApiResponse 格式)
        return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    private static string BuildErrorJson(Exception ex, string method, string path)
    {
        // 区分三类错误:超时 / 连接失败(服务未启动或端口不对) / 其它
        string kind;
        string hint;

        if (ex is TaskCanceledException)
        {
            kind = "timeout";
            hint = $"请求超时(默认 {DefaultTimeoutSec}s)。WPF 桌面端可能未响应、正在执行长操作,或端点卡住。";
        }
        else if (ex is OperationCanceledException)
        {
            kind = "cancelled";
            hint = "请求被取消。";
        }
        else if (ex is HttpRequestException hre)
        {
            kind = "connection";
            if (hre.InnerException is SocketException se)
                hint = $"无法连接到 WPF 桌面端 HTTP API。SocketError={se.SocketErrorCode}。请确认 WPF 已启动并监听 8899 端口。";
            else
                hint = $"HTTP 连接失败: {hre.Message}。请确认 WPF 桌面端已启动。";
        }
        else
        {
            kind = ex.GetType().Name;
            hint = ex.Message;
        }

        return JsonSerializer.Serialize(new
        {
            success = false,
            error = $"Proxy {method} {path} failed: {ex.Message}",
            errorKind = kind,
            hint,
            method,
            path
        }, JsonOpts);
    }
}
