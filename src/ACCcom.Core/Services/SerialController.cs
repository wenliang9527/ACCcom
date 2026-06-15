using System.Text.Json;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class SerialController : WebApiController
{
    private readonly HttpService _service;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SerialController(HttpService service) => _service = service;

    private async Task<T?> ReadBodyAsync<T>() where T : new()
    {
        try
        {
            var body = await HttpContext.GetRequestBodyAsStringAsync();
            if (string.IsNullOrEmpty(body))
                return new T();
            return JsonSerializer.Deserialize<T>(body, _jsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
        catch (InvalidOperationException)
        {
            return default;
        }
    }

    // GET /api/health
    [Route(HttpVerbs.Get, "/health")]
    public object Health() => ApiResponse.Ok(new { status = "ok", time = DateTime.Now });

    // GET /api/ports
    [Route(HttpVerbs.Get, "/ports")]
    public object GetPorts()
    {
        try
        {
            var ports = SerialService.GetAvailablePorts();
            return ApiResponse.Ok(new { ports });
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"Failed to list ports: {ex.Message}");
        }
    }

    // GET /api/status
    [Route(HttpVerbs.Get, "/status")]
    public object GetStatus() => ApiResponse.Ok(_service.GetStatus());

    // POST /api/port/open
    [Route(HttpVerbs.Post, "/port/open")]
    public async Task<object> OpenPort()
    {
        try
        {
            var req = await HttpContext.GetRequestDataAsync<OpenPortRequest>();
            if (string.IsNullOrEmpty(req.Port))
                return ApiResponse.Fail("缺少 port 参数");

            if (_service.OpenPort(req))
                return ApiResponse.Ok(new { port = req.Port, baudRate = req.BaudRate });
            else
                return ApiResponse.Fail($"打开串口 {req.Port} 失败");
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"请求解析失败: {ex.Message}");
        }
    }

    // POST /api/port/close
    [Route(HttpVerbs.Post, "/port/close")]
    public object ClosePort()
    {
        try
        {
            if (_service.ClosePort())
                return ApiResponse.Ok(new { message = "串口已关闭" });
            else
                return ApiResponse.Fail("关闭串口失败");
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"关闭串口异常: {ex.Message}");
        }
    }

    // POST /api/send  (JSON body: { data, isHex? } or plain text body)
    [Route(HttpVerbs.Post, "/send")]
    public async Task<object> SendData()
    {
        try
        {
            string data;
            bool isHex = false;

            var contentType = HttpContext.Request.ContentType ?? "";
            if (contentType.Contains("application/json"))
            {
                var req = await ReadBodyAsync<SendRequest>();
                if (req == null)
                    return ApiResponse.Fail("请求体解析失败，期望 JSON: { \"data\": \"...\", \"isHex\": false }");
                data = req.Data;
                isHex = req.IsHex;
            }
            else
            {
                data = await HttpContext.GetRequestBodyAsStringAsync() ?? "";
                if (data.StartsWith("hex:"))
                {
                    data = data[4..];
                    isHex = true;
                }
            }

            if (string.IsNullOrEmpty(data))
                return ApiResponse.Fail("发送数据不能为空");

            if (_service.SendToSerial(data, isHex))
                return ApiResponse.Ok(new { sent = data, isHex, length = isHex ? data.Replace(" ", "").Length / 2 : data.Length });
            else
                return ApiResponse.Fail("发送失败，串口可能未打开");
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"发送数据异常: {ex.Message}");
        }
    }

    // GET /api/data?since=0&limit=100&direction=
    [Route(HttpVerbs.Get, "/data")]
    public object GetData(
        [QueryField] int since = 0,
        [QueryField] int limit = 500,
        [QueryField] string? direction = null)
    {
        var entries = _service.GetEntriesSince(since);

        if (!string.IsNullOrEmpty(direction))
            entries = entries.Where(e => string.Equals(e.Direction, direction, StringComparison.OrdinalIgnoreCase)).ToList();

        if (limit > 0 && entries.Count > limit)
            entries = entries.Take(limit).ToList();

        return ApiResponse.Ok(new
        {
            entries,
            count = entries.Count,
            latestId = entries.Count > 0 ? entries[^1].Id : since
        });
    }

    // POST /api/clear  { target: "rx"|"tx"|"all" }
    [Route(HttpVerbs.Post, "/clear")]
    public async Task<object> ClearBuffer()
    {
        string? target = null;
        var req = await ReadBodyAsync<ClearRequest>();
        if (req != null)
        {
            target = req.Target;
        }
        else
        {
            // Fallback: try plain text body
            var body = await HttpContext.GetRequestBodyAsStringAsync();
            if (!string.IsNullOrEmpty(body))
                target = body.Trim().Trim('"');
        }

        if (target == "all" || string.IsNullOrEmpty(target))
            target = null; // null means clear all

        _service.ClearBuffer(target);
        return ApiResponse.Ok(new { cleared = target ?? "all" });
    }

    // POST /api/wait-for  { pattern, timeoutMs?, matchMode?, matchHex?, direction? }
    [Route(HttpVerbs.Post, "/wait-for")]
    public async Task<object> WaitForData()
    {
        try
        {
            var req = await ReadBodyAsync<WaitForRequest>();
            if (req == null)
                return ApiResponse.Fail("请求体解析失败，期望 JSON: { \"pattern\": \"...\", \"timeoutMs\": 5000 }");

            if (string.IsNullOrEmpty(req.Pattern))
                return ApiResponse.Fail("pattern 不能为空");

            var timeout = Math.Clamp(req.TimeoutMs, 100, 60000);

            var entry = await _service.Buffer.WaitForMatchAsync(
                req.Pattern, req.MatchMode, req.MatchHex, req.Direction, timeout);

            if (entry != null)
                return ApiResponse.Ok(new { matched = true, entry });
            else
                return ApiResponse.Ok(new { matched = false, message = $"等待超时 ({timeout}ms)，未匹配到数据" });
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"等待数据异常: {ex.Message}");
        }
    }

    // GET /api/slaves
    [Route(HttpVerbs.Get, "/slaves")]
    public object GetSlaves() => ApiResponse.Ok(_service.GetSlaves());

    // GET /api/parsers
    [Route(HttpVerbs.Get, "/parsers")]
    public object GetParsers() => ApiResponse.Ok(new
    {
        parsers = _service.GetAvailableParsers(),
        active = _service.GetActiveParser()
    });

    // GET /api/parser/read?name=xxx
    [Route(HttpVerbs.Get, "/parser/read")]
    public object ReadParser([QueryField] string name)
    {
        if (string.IsNullOrEmpty(name))
            return ApiResponse.Fail("缺少 name 参数");

        var code = _service.ReadParserCode(name);
        if (code == null)
            return ApiResponse.Fail($"解析器 '{name}' 未找到");

        return ApiResponse.Ok(new { name, code });
    }

    // POST /api/parser/write  { name, code }
    [Route(HttpVerbs.Post, "/parser/write")]
    public async Task<object> WriteParser()
    {
        try
        {
            var req = await ReadBodyAsync<WriteParserRequest>();
            if (req == null || string.IsNullOrEmpty(req.Name) || string.IsNullOrEmpty(req.Code))
                return ApiResponse.Fail("需要 name 和 code 参数");

            if (_service.WriteParserCode(req.Name, req.Code))
                return ApiResponse.Ok(new { message = $"解析器 '{req.Name}' 已写入" });

            return ApiResponse.Fail("写入解析器失败");
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"写入解析器异常: {ex.Message}");
        }
    }

    // POST /api/parser/parse-raw  { hex, parserName? }
    [Route(HttpVerbs.Post, "/parser/parse-raw")]
    public async Task<object> ParseRaw()
    {
        var req = await ReadBodyAsync<ParseRawRequest>();
        if (req == null || string.IsNullOrEmpty(req.Hex))
            return ApiResponse.Fail("需要 hex 参数");

        var (fields, error) = await _service.ParseRawHexAsync(req.Hex, req.ParserName);
        if (error != null)
            return ApiResponse.Fail(error);

        return ApiResponse.Ok(new { hex = req.Hex, fields, fieldCount = fields?.Count ?? 0 });
    }

    // POST /api/parser/activate  { name }
    [Route(HttpVerbs.Post, "/parser/activate")]
    public async Task<object> ActivateParser()
    {
        try
        {
            string? name = null;
            var req = await ReadBodyAsync<ActivateParserRequest>();
            if (req != null)
            {
                name = req.Name;
            }
            else
            {
                var body = await HttpContext.GetRequestBodyAsStringAsync();
                if (!string.IsNullOrEmpty(body))
                    name = body.Trim().Trim('"');
            }

            name = name?.Trim();
            if (string.IsNullOrEmpty(name) || name == ParserManager.NoParserName)
            {
                _service.ActivateParser(null);
                return ApiResponse.Ok(new { message = "已停用解析器" });
            }

            if (_service.ActivateParser(name))
                return ApiResponse.Ok(new { message = $"已激活解析器: {name}" });

            if (string.IsNullOrEmpty(_service.GetParserError()))
            {
                _service.ActivateParser(null);
                return ApiResponse.Ok(new { message = "已停用解析器" });
            }
            return ApiResponse.Fail($"解析器加载失败: {_service.GetParserError()}");
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"激活解析器异常: {ex.Message}");
        }
    }
}
