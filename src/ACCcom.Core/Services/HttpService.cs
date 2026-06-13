using System.Text.Json;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using EmbedIO.WebSockets;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class HttpService : IDisposable
{
    public const string DefaultUrl = "http://127.0.0.1:8899";

    private readonly WebServer _server;
    private readonly SerialService? _serialService;
    private readonly ParserManager? _parserManager;
    public DataBufferService Buffer { get; } = new();
    public event Action<LogEntry>? OnDataEntry;

    public HttpService(SerialService? serialService = null, ParserManager? parserManager = null, string url = DefaultUrl)
    {
        _serialService = serialService;
        _parserManager = parserManager;
        _server = new WebServer(o => o.WithUrlPrefix(url).WithMode(HttpListenerMode.EmbedIO))
            .WithWebApi("/api", m => m.WithController(() => new SerialController(this)))
            .WithModule(new SerialWebSocketHandler("/ws", this));
    }

    public void Start() => _server.Start();
    public void Stop() => _server.Dispose();

    public void AddEntry(LogEntry entry)
    {
        Buffer.AddEntry(entry);
        OnDataEntry?.Invoke(entry);
    }

    public List<LogEntry> GetEntriesSince(int id) => Buffer.GetEntriesSince(id);

    public void ClearBuffer(string? target) => Buffer.Clear(target);

    public bool SendToSerial(string data, bool isHex)
    {
        return _serialService?.Send(data, isHex) ?? false;
    }

    public bool OpenPort(OpenPortRequest req)
    {
        if (_serialService == null) return false;
        if (_serialService.IsOpen) return true;

        var config = new SerialConfig
        {
            PortName = req.Port,
            BaudRate = req.BaudRate,
            DataBits = req.DataBits,
            StopBits = req.StopBits,
            Parity = req.Parity,
            DtrEnable = req.Dtr,
            RtsEnable = req.Rts
        };
        return _serialService.Open(config);
    }

    public bool ClosePort()
    {
        return _serialService?.Close() ?? true;
    }

    public object GetStatus()
    {
        return new
        {
            isOpen = _serialService?.IsOpen ?? false,
            currentPort = _serialService?.CurrentPort,
            baudRate = _serialService?.BaudRate ?? 0,
            rxCount = GetRxCount(),
            txCount = GetTxCount(),
            bufferCount = GetBufferCount()
        };
    }

    private int GetRxCount() => Buffer.CountWhere(e => e.Direction == "RX");

    private int GetTxCount() => Buffer.CountWhere(e => e.Direction == "TX");

    private int GetBufferCount() => Buffer.Count();

    public List<string> GetAvailableParsers()
    {
        return _parserManager?.AvailableParsers.ToList() ?? new List<string>();
    }

    public bool ActivateParser(string? name)
    {
        return _parserManager?.Activate(name) ?? false;
    }

    public string? GetActiveParser()
    {
        return _parserManager?.ActiveParserName;
    }

    public string? GetParserError()
    {
        return _parserManager?.LastError;
    }

    public string? GetParserDir()
    {
        return _parserManager?.GetParserDir();
    }

    public string? ReadParserCode(string name)
    {
        var dir = _parserManager?.GetParserDir();
        if (dir == null) return null;
        var path = Path.Combine(dir, name + ".csx");
        if (!File.Exists(path)) return null;
        return File.ReadAllText(path);
    }

    public bool WriteParserCode(string name, string code)
    {
        var dir = _parserManager?.GetParserDir();
        if (dir == null) return false;
        var path = Path.Combine(dir, name + ".csx");
        File.WriteAllText(path, code);
        _parserManager?.Refresh();
        return true;
    }

    public (List<FieldAnnotation>? fields, string? error) ParseRawHex(string hex, string? parserName = null)
    {
        if (_parserManager == null) return (null, "ParserManager not available");

        byte[] data;
        try { data = Convert.FromHexString(hex.Replace(" ", "")); }
        catch { return (null, "Invalid hex string"); }

        var engine = _parserManager.Engine;
        if (!string.IsNullOrEmpty(parserName) && parserName != _parserManager.ActiveParserName)
        {
            var tempEngine = new ParserEngine();
            var dir = _parserManager.GetParserDir();
            var path = Path.Combine(dir, parserName + ".csx");
            if (!File.Exists(path)) return (null, $"Parser '{parserName}' not found");
            if (!tempEngine.Load(File.ReadAllText(path))) return (null, $"Parser load failed: {tempEngine.LastError}");
            engine = tempEngine;
        }
        if (_parserManager.ActiveParserName == null && string.IsNullOrEmpty(parserName))
            return (null, "No active parser. Activate one first or specify parserName.");

        var fields = engine.Execute(data, DateTime.Now);
        return (fields, null);
    }

    public void Dispose()
    {
        Buffer.CancelWaiters();
        _server?.Dispose();
    }
}

// --- API Controller ---

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
        catch
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

        var (fields, error) = _service.ParseRawHex(req.Hex, req.ParserName);
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

public class SerialWebSocketHandler : WebSocketModule
{
    private readonly HttpService _service;

    public SerialWebSocketHandler(string urlPath, HttpService service) : base(urlPath, true)
    {
        _service = service;
        _service.OnDataEntry += OnDataEntry;
    }

    private void OnDataEntry(LogEntry entry)
    {
        _ = BroadcastAsync(JsonSerializer.Serialize(entry, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }

    protected override Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
    {
        return Task.CompletedTask;
    }

    protected override Task OnClientConnectedAsync(IWebSocketContext context)
    {
        return Task.CompletedTask;
    }

    protected override Task OnClientDisconnectedAsync(IWebSocketContext context)
    {
        return Task.CompletedTask;
    }
}
