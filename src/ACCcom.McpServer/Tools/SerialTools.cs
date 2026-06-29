using System.ComponentModel;
using System.Text.Json;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using ModelContextProtocol.Server;

namespace ACCcom.McpServer.Tools;

[McpServerToolType]
public class SerialTools
{
    private readonly ToolContext _ctx;
    private ISerialService? _serial => _ctx.Serial;
    private ProxyClient? _proxy => _ctx.Proxy;
    private ParserManager _parserManager => _ctx.ParserManager;

    public SerialTools(ToolContext ctx)
    {
        _ctx = ctx;
    }

    [McpServerTool, Description("List all available serial ports on the system.")]
    public Task<string> ListPorts()
    {
        if (_ctx.UseProxy) return _proxy!.GetAsync("/api/ports");
        var ports = SerialService.GetAvailablePorts();
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { ports, count = ports.Length } }));
    }

    [McpServerTool, Description("Get current serial port connection status, configuration, and RX/TX counters.")]
    public Task<string> GetStatus()
    {
        if (_ctx.UseProxy) return _proxy!.GetAsync("/api/status");
        int rxCount = _ctx.Buffer.CountWhere(e => e.Direction == "RX");
        int txCount = _ctx.Buffer.CountWhere(e => e.Direction == "TX");
        return Task.FromResult(_ctx.RawJson(new
        {
            success = true,
            data = new
            {
                isOpen = _serial!.IsOpen,
                currentPort = _serial.CurrentPort,
                baudRate = _serial.BaudRate,
                rxCount, txCount,
                bufferCount = rxCount + txCount,
                activeParser = _parserManager.ActiveParserName
            }
        }));
    }

    [McpServerTool, Description("Check MCP server health and runtime status. Returns uptime, memory usage, active parser, and connection state.")]
    public async Task<string> HealthCheck()
    {
        if (_ctx.UseProxy)
        {
            var proxyResult = await _proxy!.GetAsync("/api/health").ConfigureAwait(false);
            return proxyResult;
        }

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var uptime = DateTime.Now - process.StartTime;
        return _ctx.RawJson(new
        {
            success = true,
            data = new
            {
                status = "ok",
                version = typeof(SerialTools).Assembly.GetName()?.Version?.ToString(3) ?? "1.0.0",
                uptime = uptime.ToString(@"d\.hh\:mm\:ss"),
                memoryMb = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1),
                threadCount = process.Threads.Count,
                isOpen = _serial!.IsOpen,
                currentPort = _serial.CurrentPort,
                activeParser = _parserManager.ActiveParserName,
                bufferCount = _ctx.Buffer.Count(),
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            }
        });
    }

    // ========== Multi-Port Management ==========

    [McpServerTool, Description("Open an additional serial port with a custom tag for multi-port operation. Parameters: tag (unique identifier), port, baudRate, dataBits, stopBits, parity, dtr, rts.")]
    public Task<string> OpenPortTagged(
        [Description("Unique tag for this port (e.g. 'sensor1', 'gps')")] string tag,
        [Description("Serial port name, e.g. COM3")] string port,
        [Description("Baud rate (default 115200)")] int baudRate = 115200,
        [Description("Data bits (default 8)")] int dataBits = 8,
        [Description("Stop bits: 0=None, 1=One, 2=Two (default 1)")] int stopBits = 1,
        [Description("Parity: 0=None, 1=Odd, 2=Even (default 0)")] int parity = 0,
        [Description("Enable DTR (default false)")] bool dtr = false,
        [Description("Enable RTS (default false)")] bool rts = false)
    {
        if (_ctx.UseProxy)
            return _proxy!.PostAsync("/api/multiport/open", new { tag, port, baudRate, dataBits, stopBits, parity, dtr, rts });
        if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(port))
            return Task.FromResult(_ctx.RawJson(new { success = false, error = "Both tag and port are required" }));
        var config = new SerialConfig { PortName = port, BaudRate = baudRate, DataBits = dataBits, StopBits = stopBits, Parity = parity, DtrEnable = dtr, RtsEnable = rts };
        if (_ctx.MultiPort!.OpenPort(tag, config))
            return Task.FromResult(_ctx.RawJson(new { success = true, data = new { tag, port, baudRate } }));
        return Task.FromResult(_ctx.RawJson(new { success = false, error = $"Failed to open port {port} with tag {tag}" }));
    }

    [McpServerTool, Description("Close a tagged port opened via open_port_tagged. Parameters: tag.")]
    public Task<string> ClosePortTagged(
        [Description("Tag of the port to close")] string tag)
    {
        if (_ctx.UseProxy)
            return _proxy!.PostAsync("/api/multiport/close", new { tag });
        if (_ctx.MultiPort!.ClosePort(tag))
            return Task.FromResult(_ctx.RawJson(new { success = true, data = new { message = $"Port '{tag}' closed", tag } }));
        return Task.FromResult(_ctx.RawJson(new { success = false, error = $"Failed to close port '{tag}'" }));
    }

    [McpServerTool, Description("Send data to a specific tagged port. Parameters: tag, data, isHex.")]
    public Task<string> SendToPort(
        [Description("Tag of the target port")] string tag,
        [Description("Data to send")] string data,
        [Description("Send as hex (default false)")] bool isHex = false)
    {
        if (_ctx.UseProxy)
            return _proxy!.PostAsync("/api/multiport/send", new { tag, data, isHex });
        if (_ctx.MultiPort!.SendToPort(tag, data, isHex))
            return Task.FromResult(_ctx.RawJson(new { success = true, data = new { tag, sent = data, isHex } }));
        return Task.FromResult(_ctx.RawJson(new { success = false, error = $"Send failed on port '{tag}'" }));
    }

    [McpServerTool, Description("Open a serial port with specified configuration. Parameters: port (required, e.g. COM3), baudRate (default 115200), dataBits (default 8), stopBits (0=None,1=One,2=Two, default 1), parity (0=None,1=Odd,2=Even, default 0), dtr (default false), rts (default false).")]
    public async Task<string> OpenPort(
        [Description("Serial port name, e.g. COM3")] string port,
        [Description("Baud rate (default 115200)")] int baudRate = 115200,
        [Description("Data bits (default 8)")] int dataBits = 8,
        [Description("Stop bits: 0=None, 1=One, 2=Two (default 1)")] int stopBits = 1,
        [Description("Parity: 0=None, 1=Odd, 2=Even (default 0)")] int parity = 0,
        [Description("Enable DTR (default false)")] bool dtr = false,
        [Description("Enable RTS (default false)")] bool rts = false)
    {
        if (_ctx.UseProxy)
            return await _proxy!.PostAsync("/api/port/open", new { port, baudRate, dataBits, stopBits, parity, dtr, rts }).ConfigureAwait(false);

        if (string.IsNullOrEmpty(port))
            return _ctx.RawJson(new { success = false, error = "Port name is required (e.g. COM3)" });
        if (_serial!.IsOpen)
            return _ctx.RawJson(new { success = true, data = new { message = "Port already open", port = _serial.CurrentPort } });

        var config = new SerialConfig { PortName = port, BaudRate = baudRate, DataBits = dataBits, StopBits = stopBits, Parity = parity, DtrEnable = dtr, RtsEnable = rts };
        if (_serial.Open(config))
            return _ctx.RawJson(new { success = true, data = new { port, baudRate, dataBits } });
        return _ctx.RawJson(new { success = false, error = $"Failed to open port {port}" });
    }

    [McpServerTool, Description("Close the currently open serial port.")]
    public Task<string> ClosePort()
    {
        if (_ctx.UseProxy) return _proxy!.PostAsync("/api/port/close");
        if (_serial!.Close())
            return Task.FromResult(_ctx.RawJson(new { success = true, data = new { message = "Port closed" } }));
        return Task.FromResult(_ctx.RawJson(new { success = false, error = "Failed to close port" }));
    }

    // ========== Data Send / Receive ==========

    [McpServerTool, Description("Send data to the serial port. Parameters: data (the text or hex string to send), isHex (if true, data is treated as hex bytes, default false). Returns success status.")]
    public Task<string> Send(
        [Description("Data to send (ASCII text or hex string)")] string data,
        [Description("Send as hex bytes (default false)")] bool isHex = false)
    {
        if (_ctx.UseProxy) return _proxy!.PostAsync("/api/send", new { data, isHex });
        if (string.IsNullOrEmpty(data))
            return Task.FromResult(_ctx.RawJson(new { success = false, error = "Data cannot be empty" }));
        if (_serial!.Send(data, isHex))
            return Task.FromResult(_ctx.RawJson(new { success = true, data = new { sent = data, isHex, byteLength = isHex ? data.Replace(" ", "").Length / 2 : data.Length } }));
        return Task.FromResult(_ctx.RawJson(new { success = false, error = "Send failed, port may not be open" }));
    }

    [McpServerTool, Description("Read serial port data from the buffer. Parameters: sinceId (return entries with ID > sinceId, default 0), limit (max entries to return, default 100), direction (filter by RX/TX, null for all).")]
    public async Task<string> ReadData(
        [Description("Return entries with ID greater than this (default 0)")] int sinceId = 0,
        [Description("Maximum number of entries to return (default 100)")] int limit = 100,
        [Description("Filter by direction: RX or TX (null for all)")] string? direction = null)
    {
        if (_ctx.UseProxy)
        {
            var query = $"/api/data?since={sinceId}&limit={limit}";
            if (!string.IsNullOrEmpty(direction)) query += $"&direction={direction}";
            return await _proxy!.GetAsync(query).ConfigureAwait(false);
        }

        var entries = _ctx.Buffer.GetEntriesSince(sinceId);
        if (!string.IsNullOrEmpty(direction))
            entries = entries.Where(e => string.Equals(e.Direction, direction, StringComparison.OrdinalIgnoreCase)).ToList();
        if (limit > 0 && entries.Count > limit) entries = entries.Take(limit).ToList();
        return _ctx.RawJson(new { success = true, data = new { entries, count = entries.Count, latestId = entries.Count > 0 ? entries[^1].Id : sinceId } });
    }

    [McpServerTool, Description("Wait for data matching a pattern. Blocks until match or timeout. Parameters: pattern (string to match), timeoutMs (max wait in ms, default 5000, max 60000), matchMode (contains/regex/exact, default contains), matchHex (match against hex data instead of text, default false), direction (RX/TX filter, null for any).")]
    public async Task<string> WaitForResponse(
        [Description("Pattern to match in received data")] string pattern,
        [Description("Timeout in milliseconds (default 5000, max 60000)")] int timeoutMs = 5000,
        [Description("Match mode: contains, regex, or exact (default contains)")] string matchMode = "contains",
        [Description("Match against hex data instead of text (default false)")] bool matchHex = false,
        [Description("Filter direction: RX or TX (null for any)")] string? direction = null)
    {
        if (_ctx.UseProxy)
            return await _proxy!.PostAsync("/api/wait-for", new { pattern, timeoutMs, matchMode, matchHex, direction }).ConfigureAwait(false);

        if (string.IsNullOrEmpty(pattern))
            return _ctx.RawJson(new { success = false, error = "Pattern is required" });
        var timeout = Math.Clamp(timeoutMs, 100, 60000);
        var entry = await WaitForDataInternalAsync(pattern, matchMode, matchHex, direction, timeout).ConfigureAwait(false);
        if (entry != null)
            return _ctx.RawJson(new { success = true, data = new { matched = true, entry } });
        return _ctx.RawJson(new { success = true, data = new { matched = false, message = $"Timeout ({timeout}ms), no matching data found" } });
    }

    internal Task<LogEntry?> WaitForDataInternalAsync(string pattern, string matchMode, bool matchHex, string? direction, int timeoutMs)
    {
        return _ctx.Buffer.WaitForMatchAsync(pattern, matchMode, matchHex, direction, timeoutMs);
    }

    [McpServerTool, Description("Send data to serial port and wait for a matching response. Combines send + wait_for_response in one call. Parameters: data (text or hex to send), pattern (response pattern to match), isHex (default false), timeoutMs (default 5000, max 60000), matchMode (contains/regex/exact, default contains), matchHex (match against hex data, default false), direction (RX/TX filter, default RX).")]
    public async Task<string> SendAndWait(
        [Description("Data to send (ASCII text or hex string)")] string data,
        [Description("Pattern to match in response")] string pattern,
        [Description("Send as hex bytes (default false)")] bool isHex = false,
        [Description("Timeout in milliseconds (default 5000, max 60000)")] int timeoutMs = 5000,
        [Description("Match mode: contains, regex, or exact (default contains)")] string matchMode = "contains",
        [Description("Match against hex data instead of text (default false)")] bool matchHex = false,
        [Description("Filter direction: RX or TX (default RX)")] string? direction = "RX")
    {
        if (_ctx.UseProxy)
        {
            // For proxy mode, send then wait via HTTP
            var sendResult = await _proxy!.PostAsync("/api/send", new { data, isHex }).ConfigureAwait(false);
            using var sendDoc = JsonDocument.Parse(sendResult);
            if (!sendDoc.RootElement.GetProperty("success").GetBoolean())
                return sendResult;
            return await _proxy.PostAsync("/api/wait-for", new { pattern, timeoutMs, matchMode, matchHex, direction }).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(data))
            return _ctx.RawJson(new { success = false, error = "Data cannot be empty" });
        if (string.IsNullOrEmpty(pattern))
            return _ctx.RawJson(new { success = false, error = "Pattern is required" });

        // Register waiter BEFORE sending to avoid race condition
        var timeout = Math.Clamp(timeoutMs, 100, 60000);
        var waiterTask = WaitForDataInternalAsync(pattern, matchMode, matchHex, direction ?? "RX", timeout);

        if (!_serial!.Send(data, isHex))
            return _ctx.RawJson(new { success = false, error = "Send failed, port may not be open" });

        var entry = await waiterTask.ConfigureAwait(false);
        if (entry != null)
            return _ctx.RawJson(new { success = true, data = new { sent = data, isHex, matched = true, response = entry } });
        return _ctx.RawJson(new { success = true, data = new { sent = data, isHex, matched = false, message = $"Timeout ({timeout}ms), no matching response" } });
    }

    [McpServerTool, Description("Send multiple commands in sequence and collect responses. Parameters: commands (array of objects with 'data', 'isHex', 'waitMs' fields), responsePattern (pattern to match for each response, default 'OK'), timeoutMs (per-command timeout, default 3000).")]
    public async Task<string> SendBatch(
        [Description("Array of commands: [{\"data\":\"AT+GMR\",\"waitMs\":1000}, ...]")] string commands,
        [Description("Response pattern to match after each command (default 'OK')")] string responsePattern = "OK",
        [Description("Per-command timeout in ms (default 3000)")] int timeoutMs = 3000)
    {
        if (_ctx.UseProxy)
            return await _proxy!.PostAsync("/api/send-batch", new { commands, responsePattern, timeoutMs }).ConfigureAwait(false);

        if (string.IsNullOrEmpty(commands))
            return _ctx.RawJson(new { success = false, error = "Commands array is required" });

        List<Models.BatchCommand> cmdList;
        try
        {
            cmdList = JsonSerializer.Deserialize<List<Models.BatchCommand>>(commands, ToolContext.JsonOpts) ?? new();
        }
        catch (Exception ex)
        {
            return _ctx.RawJson(new { success = false, error = $"Invalid commands JSON: {ex.Message}" });
        }

        if (cmdList.Count == 0)
            return _ctx.RawJson(new { success = false, error = "Commands array is empty" });

        var results = new List<object>();
        foreach (var cmd in cmdList)
        {
            if (string.IsNullOrEmpty(cmd.Data))
            {
                results.Add(new { sent = "", success = false, error = "Empty command" });
                continue;
            }

            // Register waiter before send
            var timeout = Math.Clamp(cmd.WaitMs > 0 ? cmd.WaitMs : timeoutMs, 100, 60000);
            var waiterTask = WaitForDataInternalAsync(responsePattern, "contains", false, "RX", timeout);

            var sent = _serial!.Send(cmd.Data, cmd.IsHex);
            if (!sent)
            {
                results.Add(new { sent = cmd.Data, success = false, error = "Send failed" });
                continue;
            }

            var entry = await waiterTask.ConfigureAwait(false);
            results.Add(new
            {
                sent = cmd.Data,
                success = true,
                matched = entry != null,
                response = entry?.Text,
                responseHex = entry?.RawHex
            });
        }

        return _ctx.RawJson(new { success = true, data = new { count = results.Count, results } });
    }

    [McpServerTool, Description("Clear the data buffer. Parameters: target (rx/tx/all, default all).")]
    public async Task<string> ClearBuffer(
        [Description("What to clear: rx, tx, or all (default all)")] string? target = null)
    {
        if (_ctx.UseProxy) return await _proxy!.PostAsync("/api/clear", new { target = target ?? "all" }).ConfigureAwait(false);
        _ctx.Buffer.Clear(target);
        return _ctx.RawJson(new { success = true, data = new { cleared = target ?? "all" } });
    }

    [McpServerTool, Description("Get RX data statistics: data rate (bytes/sec), error rate (%), frame intervals, and totals.")]
    public async Task<string> GetStatistics()
    {
        if (_ctx.UseProxy)
            return await _proxy!.GetAsync("/api/statistics").ConfigureAwait(false);

        return _ctx.RawJson(new
        {
            success = true,
            data = new
            {
                rxBytesPerSec = Math.Round(_ctx.Stats!.RxBytesPerSecond, 1),
                rxFramesPerSec = Math.Round(_ctx.Stats.RxFramesPerSecond, 1),
                errorRatePercent = Math.Round(_ctx.Stats.ErrorRate, 2),
                avgFrameIntervalMs = Math.Round(_ctx.Stats.AvgFrameIntervalMs, 2),
                totalRxBytes = _ctx.Stats.TotalRxBytes,
                totalRxFrames = _ctx.Stats.TotalRxFrames,
                totalErrorFrames = _ctx.Stats.TotalErrorFrames
            }
        });
    }

    // ========== Auto Baud Rate Detection ==========

    [McpServerTool, Description("Auto-detect the baud rate of a serial device. Probes common baud rates and returns the detected one. Parameters: port (serial port name, e.g. COM3).")]
    public async Task<string> DetectBaudRate(
        [Description("Serial port name, e.g. COM3")] string port)
    {
        if (_ctx.UseProxy)
            return await _proxy!.PostAsync("/api/baud/detect", new { port }).ConfigureAwait(false);

        if (string.IsNullOrEmpty(port))
            return _ctx.RawJson(new { success = false, error = "Port name is required (e.g. COM3)" });

        if (_serial!.IsOpen)
            return _ctx.RawJson(new { success = false, error = "Close the current port before running auto baud detection" });

        var autoBaud = _ctx.AutoBaud;
        if (autoBaud == null)
            return _ctx.RawJson(new { success = false, error = "AutoBaudDetector not available in this mode" });

        try
        {
            var detected = await autoBaud.DetectAsync(port).ConfigureAwait(false);
            if (detected > 0)
                return _ctx.RawJson(new { success = true, data = new { port, baudRate = detected, message = $"Detected baud rate: {detected}" } });
            return _ctx.RawJson(new { success = true, data = new { port, baudRate = 0, message = "No baud rate detected. Device may not be responding." } });
        }
        catch (Exception ex)
        {
            return _ctx.RawJson(new { success = false, error = $"Auto baud detection failed: {ex.Message}" });
        }
    }
}
