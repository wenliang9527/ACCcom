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

    // ========== Multi-Port ==========

    // POST /api/multiport/open
    [Route(HttpVerbs.Post, "/multiport/open")]
    public async Task<object> MultiPortOpen()
    {
        try
        {
            var req = await ReadBodyAsync<MultiPortOpenRequest>();
            if (req == null || string.IsNullOrEmpty(req.Tag) || string.IsNullOrEmpty(req.Port))
                return ApiResponse.Fail("需要 tag 和 port 参数");

            if (_service.MultiPortOpen(req))
                return ApiResponse.Ok(new { tag = req.Tag, port = req.Port, baudRate = req.BaudRate });
            return ApiResponse.Fail($"打开端口 {req.Port} (tag={req.Tag}) 失败");
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"多端口打开异常: {ex.Message}");
        }
    }

    // POST /api/multiport/close
    [Route(HttpVerbs.Post, "/multiport/close")]
    public async Task<object> MultiPortClose()
    {
        try
        {
            var req = await ReadBodyAsync<MultiPortTagRequest>();
            var tag = req?.Tag ?? "";
            if (string.IsNullOrEmpty(tag))
                return ApiResponse.Fail("需要 tag 参数");

            if (_service.MultiPortClose(tag))
                return ApiResponse.Ok(new { message = $"端口 '{tag}' 已关闭", tag });
            return ApiResponse.Fail($"关闭端口 '{tag}' 失败");
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"多端口关闭异常: {ex.Message}");
        }
    }

    // POST /api/multiport/send
    [Route(HttpVerbs.Post, "/multiport/send")]
    public async Task<object> MultiPortSend()
    {
        try
        {
            var req = await ReadBodyAsync<MultiPortSendRequest>();
            if (req == null || string.IsNullOrEmpty(req.Tag) || string.IsNullOrEmpty(req.Data))
                return ApiResponse.Fail("需要 tag 和 data 参数");

            if (_service.MultiPortSend(req.Tag, req.Data, req.IsHex))
                return ApiResponse.Ok(new { tag = req.Tag, sent = req.Data, isHex = req.IsHex });
            return ApiResponse.Fail($"向端口 '{req.Tag}' 发送失败");
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"多端口发送异常: {ex.Message}");
        }
    }

    // ========== Modbus ==========

    // POST /api/modbus/read
    [Route(HttpVerbs.Post, "/modbus/read")]
    public async Task<object> ModbusRead()
    {
        try
        {
            var req = await ReadBodyAsync<ModbusReadRequest>();
            if (req == null)
                return ApiResponse.Fail("请求体解析失败");

            var result = await _service.ReadRegistersAsync(req).ConfigureAwait(false);
            if (result.IsError)
                return ApiResponse.Fail(result.ErrorMessage ?? "MODBUS 读取错误");

            var registers = new List<object>();
            for (int i = 0; i + 1 < result.Data.Length && registers.Count < req.Quantity; i += 2)
            {
                var val = (ushort)((result.Data[i] << 8) | result.Data[i + 1]);
                registers.Add(new { address = (ushort)(req.StartAddress + registers.Count), value = val, hex = $"0x{val:X4}" });
            }

            return ApiResponse.Ok(new
            {
                slaveId = (int)result.SlaveId,
                functionCode = result.FunctionCode.ToString(),
                registerCount = registers.Count,
                registers
            });
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"MODBUS 读取异常: {ex.Message}");
        }
    }

    // POST /api/modbus/write
    [Route(HttpVerbs.Post, "/modbus/write")]
    public async Task<object> ModbusWrite()
    {
        try
        {
            var req = await ReadBodyAsync<ModbusWriteRequest>();
            if (req == null)
                return ApiResponse.Fail("请求体解析失败");

            var result = await _service.WriteRegisterAsync(req).ConfigureAwait(false);
            if (result.IsError)
                return ApiResponse.Fail(result.ErrorMessage ?? "MODBUS 写入错误");

            return ApiResponse.Ok(new
            {
                slaveId = (int)result.SlaveId,
                functionCode = result.FunctionCode.ToString(),
                address = req.Address,
                value = req.Value,
                hex = $"0x{req.Value:X4}"
            });
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"MODBUS 写入异常: {ex.Message}");
        }
    }

    // POST /api/modbus/scan
    [Route(HttpVerbs.Post, "/modbus/scan")]
    public async Task<object> ModbusScan()
    {
        try
        {
            var req = await ReadBodyAsync<ModbusScanRequest>();
            if (req == null)
                return ApiResponse.Fail("请求体解析失败");

            var devices = await _service.ScanModbusDevicesAsync(req).ConfigureAwait(false);
            return ApiResponse.Ok(new
            {
                scannedRange = $"{req.StartAddress}-{req.EndAddress}",
                timeoutMs = req.TimeoutMs,
                deviceCount = devices.Count,
                devices
            });
        }
        catch (OperationCanceledException)
        {
            return ApiResponse.Fail("扫描已取消");
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"MODBUS 扫描异常: {ex.Message}");
        }
    }

    // POST /api/slave/create
    [Route(HttpVerbs.Post, "/slave/create")]
    public async Task<object> SlaveCreate()
    {
        try
        {
            var req = await ReadBodyAsync<SlaveCreateRequest>();
            if (req == null)
                return ApiResponse.Fail("请求体解析失败");

            var id = _service.SlaveCreate(req);
            if (id == null)
                return ApiResponse.Fail("从站服务不可用或创建失败");

            return ApiResponse.Ok(new { id, slaveId = req.SlaveId, transport = req.Transport, connectionParam = req.ConnectionParam });
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"创建从站异常: {ex.Message}");
        }
    }

    // POST /api/slave/remove
    [Route(HttpVerbs.Post, "/slave/remove")]
    public async Task<object> SlaveRemove()
    {
        try
        {
            var req = await ReadBodyAsync<MultiPortTagRequest>();
            var slaveId = req?.Tag ?? "";
            if (string.IsNullOrEmpty(slaveId))
                return ApiResponse.Fail("需要 slaveId(通过 tag 字段传入)");

            if (_service.SlaveRemove(slaveId))
                return ApiResponse.Ok(new { slaveId, removed = true });
            return ApiResponse.Fail($"移除从站 '{slaveId}' 失败");
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"移除从站异常: {ex.Message}");
        }
    }

    // GET /api/slave/list
    [Route(HttpVerbs.Get, "/slave/list")]
    public object SlaveList()
    {
        var slaves = _service.SlaveList();
        if (slaves == null)
            return ApiResponse.Fail("从站服务不可用");
        return ApiResponse.Ok(new { count = slaves.Count, slaves });
    }

    // POST /api/slave/write
    [Route(HttpVerbs.Post, "/slave/write")]
    public async Task<object> SlaveWrite()
    {
        try
        {
            var req = await ReadBodyAsync<SlaveWriteRequest>();
            if (req == null || string.IsNullOrEmpty(req.SlaveId))
                return ApiResponse.Fail("需要 slaveId、type、address、value");

            if (_service.SlaveWrite(req))
                return ApiResponse.Ok(new { slaveId = req.SlaveId, type = req.Type, address = req.Address, value = req.Value });
            return ApiResponse.Fail("从站服务不可用");
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"从站写入异常: {ex.Message}");
        }
    }

    // POST /api/slave/read
    [Route(HttpVerbs.Post, "/slave/read")]
    public async Task<object> SlaveRead()
    {
        try
        {
            var req = await ReadBodyAsync<SlaveReadRequest>();
            if (req == null || string.IsNullOrEmpty(req.SlaveId))
                return ApiResponse.Fail("需要 slaveId、type、address");

            var (value, ok) = _service.SlaveRead(req);
            if (!ok)
                return ApiResponse.Fail("从站服务不可用");
            return ApiResponse.Ok(new { slaveId = req.SlaveId, type = req.Type, address = req.Address, value });
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"从站读取异常: {ex.Message}");
        }
    }

    // ========== Auto Baud ==========

    // POST /api/baud/detect
    [Route(HttpVerbs.Post, "/baud/detect")]
    public async Task<object> DetectBaudRate()
    {
        try
        {
            var req = await ReadBodyAsync<BaudDetectRequest>();
            if (req == null || string.IsNullOrEmpty(req.Port))
                return ApiResponse.Fail("需要 port 参数");

            var detected = await _service.DetectBaudRateAsync(req.Port).ConfigureAwait(false);
            if (detected < 0)
                return ApiResponse.Fail("自动波特率检测不可用(服务未注入)");
            return ApiResponse.Ok(new { port = req.Port, baudRate = detected, message = detected > 0 ? $"检测到波特率: {detected}" : "未检测到波特率" });
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"波特率检测异常: {ex.Message}");
        }
    }

    // ========== Statistics ==========

    // GET /api/statistics
    [Route(HttpVerbs.Get, "/statistics")]
    public object GetStatistics()
    {
        var stats = _service.GetStatistics();
        if (stats == null)
            return ApiResponse.Fail("统计服务不可用");
        return ApiResponse.Ok(stats);
    }

    // ========== Recording ==========

    // POST /api/recording/start
    [Route(HttpVerbs.Post, "/recording/start")]
    public async Task<object> RecordingStart()
    {
        try
        {
            var req = await ReadBodyAsync<RecordingStartRequest>();
            var filename = req?.Filename;

            var (ok, file) = _service.RecordingStart(filename);
            if (ok)
                return ApiResponse.Ok(new { message = "录制已开始", file });
            return ApiResponse.Fail(file != null ? $"录制已在进行中: {file}" : "录制启动失败(服务未注入?)");
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"启动录制异常: {ex.Message}");
        }
    }

    // POST /api/recording/stop
    [Route(HttpVerbs.Post, "/recording/stop")]
    public object RecordingStop()
    {
        try
        {
            var (ok, file, count) = _service.RecordingStop();
            if (ok)
                return ApiResponse.Ok(new { message = "录制已停止", file, recordedCount = count });
            return ApiResponse.Fail(file != null ? "停止失败" : "未在录制");
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"停止录制异常: {ex.Message}");
        }
    }

    // POST /api/recording/replay
    [Route(HttpVerbs.Post, "/recording/replay")]
    public async Task<object> RecordingReplay()
    {
        try
        {
            var req = await ReadBodyAsync<RecordingReplayRequest>();
            if (req == null || string.IsNullOrEmpty(req.Filename))
                return ApiResponse.Fail("需要 filename 参数");

            var entries = _service.RecordingReplay(req.Filename);
            return ApiResponse.Ok(new { file = req.Filename, entries, count = entries.Count });
        }
        catch (Exception ex)
        {
            return ApiResponse.Fail($"回放异常: {ex.Message}");
        }
    }

    // GET /api/recording/status
    [Route(HttpVerbs.Get, "/recording/status")]
    public object RecordingStatus()
    {
        var (isRecording, file, count) = _service.GetRecordingStatus();
        return ApiResponse.Ok(new { isRecording, file, recordedCount = count });
    }
}
