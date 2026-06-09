using System.Text.RegularExpressions;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class HttpService : IDisposable
{
    private readonly WebServer _server;
    private readonly SerialService? _serialService;
    private readonly ParserManager? _parserManager;
    private readonly object _lock = new();
    private readonly List<LogEntry> _buffer = new();
    private readonly List<DataWaiter> _waiters = new();

    public HttpService(SerialService? serialService = null, ParserManager? parserManager = null, string url = "http://127.0.0.1:8899")
    {
        _serialService = serialService;
        _parserManager = parserManager;
        _server = new WebServer(o => o.WithUrlPrefix(url).WithMode(HttpListenerMode.EmbedIO))
            .WithWebApi("/api", m => m.WithController(() => new SerialController(this)));
    }

    public void Start() => _server.Start();
    public void Stop() => _server.Dispose();

    public void AddEntry(LogEntry entry)
    {
        List<DataWaiter> toCheck;
        lock (_lock)
        {
            _buffer.Add(entry);
            toCheck = _waiters.ToList();
        }

        // Check waiters outside lock to avoid holding lock during task completion
        foreach (var waiter in toCheck)
        {
            if (!waiter.Completed && waiter.Matches(entry))
            {
                waiter.Tcs.TrySetResult(entry);
            }
        }

        // Cleanup completed waiters
        lock (_lock)
        {
            _waiters.RemoveAll(w => w.Completed || w.Tcs.Task.IsCompleted);
        }
    }

    public List<LogEntry> GetEntriesSince(int id)
    {
        lock (_lock) { return _buffer.Where(e => e.Id > id).ToList(); }
    }

    public void ClearBuffer(string? target)
    {
        lock (_lock)
        {
            if (target == "rx" || target == null)
                _buffer.RemoveAll(e => e.Direction == "RX");
            if (target == "tx" || target == null)
                _buffer.RemoveAll(e => e.Direction == "TX");
        }
    }

    public Task<LogEntry?> WaitForDataAsync(string pattern, string matchMode, bool matchHex, string? direction, int timeoutMs, CancellationToken ct = default)
    {
        var waiter = new DataWaiter
        {
            Pattern = pattern,
            MatchMode = matchMode,
            MatchHex = matchHex,
            Direction = direction,
            Tcs = new TaskCompletionSource<LogEntry?>(TaskCreationOptions.RunContinuationsAsynchronously)
        };

        lock (_lock)
        {
            _waiters.Add(waiter);
        }

        var delayTask = Task.Delay(timeoutMs, ct);
        return Task.WhenAny(waiter.Tcs.Task, delayTask).ContinueWith(_ =>
        {
            if (waiter.Tcs.Task.IsCompleted)
                return waiter.Tcs.Task.Result;
            waiter.Tcs.TrySetResult(null);
            return (LogEntry?)null;
        });
    }

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

    private int GetRxCount()
    {
        lock (_lock) { return _buffer.Count(e => e.Direction == "RX"); }
    }

    private int GetTxCount()
    {
        lock (_lock) { return _buffer.Count(e => e.Direction == "TX"); }
    }

    private int GetBufferCount()
    {
        lock (_lock) { return _buffer.Count; }
    }

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

    public void Dispose() => _server?.Dispose();
}

// --- Data Waiter for wait-for endpoint ---

public class DataWaiter
{
    public string Pattern { get; set; } = "";
    public string MatchMode { get; set; } = "contains";
    public bool MatchHex { get; set; }
    public string? Direction { get; set; }
    public TaskCompletionSource<LogEntry?> Tcs { get; set; } = new();
    public bool Completed => Tcs.Task.IsCompleted;

    public bool Matches(LogEntry entry)
    {
        if (!string.IsNullOrEmpty(Direction) &&
            !string.Equals(entry.Direction, Direction, StringComparison.OrdinalIgnoreCase))
            return false;

        var target = MatchHex ? entry.RawHex : entry.Text;
        if (string.IsNullOrEmpty(target)) return false;

        return MatchMode.ToLower() switch
        {
            "exact" => string.Equals(target, Pattern, StringComparison.OrdinalIgnoreCase),
            "regex" => TryRegexMatch(target, Pattern),
            _ => target.Contains(Pattern) // "contains" is default
        };
    }

    private static bool TryRegexMatch(string input, string pattern)
    {
        try
        {
            return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

// --- API Controller ---

public class SerialController : WebApiController
{
    private readonly HttpService _service;

    public SerialController(HttpService service) => _service = service;

    // GET /api/health
    [Route(HttpVerbs.Get, "/health")]
    public object Health() => ApiResponse.Ok(new { status = "ok", time = DateTime.Now });

    // GET /api/ports
    [Route(HttpVerbs.Get, "/ports")]
    public object GetPorts() => ApiResponse.Ok(new { ports = SerialService.GetAvailablePorts() });

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
        if (_service.ClosePort())
            return ApiResponse.Ok(new { message = "串口已关闭" });
        else
            return ApiResponse.Fail("关闭串口失败");
    }

    // POST /api/send  (JSON body: { data, isHex? } or plain text body)
    [Route(HttpVerbs.Post, "/send")]
    public async Task<object> SendData()
    {
        string data;
        bool isHex = false;

        var contentType = HttpContext.Request.ContentType ?? "";
        if (contentType.Contains("application/json"))
        {
            try
            {
                var req = await HttpContext.GetRequestDataAsync<SendRequest>();
                data = req.Data;
                isHex = req.IsHex;
            }
            catch
            {
                return ApiResponse.Fail("请求体解析失败，期望 JSON: { \"data\": \"...\", \"isHex\": false }");
            }
        }
        else
        {
            // Plain text body (backward compatible)
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
        try
        {
            var req = await HttpContext.GetRequestDataAsync<ClearRequest>();
            target = req.Target;
        }
        catch
        {
            // Fallback: try plain text body
            try
            {
                var body = await HttpContext.GetRequestBodyAsStringAsync();
                if (!string.IsNullOrEmpty(body))
                    target = body.Trim().Trim('"');
            }
            catch { }
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
        WaitForRequest req;
        try
        {
            req = await HttpContext.GetRequestDataAsync<WaitForRequest>();
        }
        catch
        {
            return ApiResponse.Fail("请求体解析失败，期望 JSON: { \"pattern\": \"...\", \"timeoutMs\": 5000 }");
        }

        if (string.IsNullOrEmpty(req.Pattern))
            return ApiResponse.Fail("pattern 不能为空");

        var timeout = Math.Clamp(req.TimeoutMs, 100, 60000);

        using var cts = new CancellationTokenSource();
        var entry = await _service.WaitForDataAsync(
            req.Pattern, req.MatchMode, req.MatchHex, req.Direction, timeout, cts.Token);

        if (entry != null)
            return ApiResponse.Ok(new { matched = true, entry });
        else
            return ApiResponse.Ok(new { matched = false, message = $"等待超时 ({timeout}ms)，未匹配到数据" });
    }

    // GET /api/parsers
    [Route(HttpVerbs.Get, "/parsers")]
    public object GetParsers() => ApiResponse.Ok(new
    {
        parsers = _service.GetAvailableParsers(),
        active = _service.GetActiveParser()
    });

    // POST /api/parser/activate  { name }
    [Route(HttpVerbs.Post, "/parser/activate")]
    public async Task<object> ActivateParser()
    {
        string? name = null;
        try
        {
            var req = await HttpContext.GetRequestDataAsync<ActivateParserRequest>();
            name = req.Name;
        }
        catch
        {
            // Fallback: try plain text body
            try
            {
                var body = await HttpContext.GetRequestBodyAsStringAsync();
                if (!string.IsNullOrEmpty(body))
                    name = body.Trim().Trim('"');
            }
            catch { }
        }

        if (string.IsNullOrEmpty(name) || name == "(无)")
        {
            _service.ActivateParser(null);
            return ApiResponse.Ok(new { message = "已停用解析器" });
        }

        if (_service.ActivateParser(name))
            return ApiResponse.Ok(new { message = $"已激活解析器: {name}" });
        else
            return ApiResponse.Fail($"解析器加载失败: {_service.GetParserError()}");
    }
}
