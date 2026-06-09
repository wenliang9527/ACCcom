using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

// --- ACCCOM MCP Server ---
// Serial port debugging tool for AI clients via Model Context Protocol (stdio).

var builder = Host.CreateApplicationBuilder(args);

// Register Core services
builder.Services.AddSingleton<SerialService>();
builder.Services.AddSingleton<ParserManager>(sp =>
{
    var parserDir = args.SkipWhile(a => a != "--parsers-dir").Skip(1).FirstOrDefault();
    return new ParserManager(parserDir);
});
builder.Services.AddSingleton<LoggerService>();

// Register MCP tools
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<ACCcomTools>();

var app = builder.Build();
await app.RunAsync();

// --- MCP Tool Definitions ---

[McpServerToolType]
public class ACCcomTools
{
    private readonly SerialService _serial;
    private readonly ParserManager _parserManager;
    private readonly LoggerService _logger;
    private readonly object _lock = new();
    private readonly List<LogEntry> _buffer = new();

    public ACCcomTools(SerialService serial, ParserManager parserManager, LoggerService logger)
    {
        _serial = serial;
        _parserManager = parserManager;
        _logger = logger;

        // Wire up serial data to our buffer + logger
        _serial.OnDataReceived += entry =>
        {
            lock (_lock) { _buffer.Add(entry); }
            _logger.Write(entry);
        };
    }

    // ========== Serial Port Management ==========

    [McpServerTool, Description("List all available serial ports on the system.")]
    public string ListPorts()
    {
        var ports = SerialService.GetAvailablePorts();
        return JsonSerializer.Serialize(new { success = true, data = new { ports, count = ports.Length } });
    }

    [McpServerTool, Description("Get current serial port connection status, configuration, and RX/TX counters.")]
    public string GetStatus()
    {
        int rxCount, txCount;
        lock (_lock)
        {
            rxCount = _buffer.Count(e => e.Direction == "RX");
            txCount = _buffer.Count(e => e.Direction == "TX");
        }
        return JsonSerializer.Serialize(new
        {
            success = true,
            data = new
            {
                isOpen = _serial.IsOpen,
                currentPort = _serial.CurrentPort,
                baudRate = _serial.BaudRate,
                rxCount,
                txCount,
                bufferCount = rxCount + txCount,
                activeParser = _parserManager.ActiveParserName
            }
        });
    }

    [McpServerTool, Description("Open a serial port with specified configuration. Parameters: port (required, e.g. COM3), baudRate (default 115200), dataBits (default 8), stopBits (0=None,1=One,2=Two, default 1), parity (0=None,1=Odd,2=Even, default 0), dtr (default false), rts (default false).")]
    public string OpenPort(
        [Description("Serial port name, e.g. COM3")] string port,
        [Description("Baud rate (default 115200)")] int baudRate = 115200,
        [Description("Data bits (default 8)")] int dataBits = 8,
        [Description("Stop bits: 0=None, 1=One, 2=Two (default 1)")] int stopBits = 1,
        [Description("Parity: 0=None, 1=Odd, 2=Even (default 0)")] int parity = 0,
        [Description("Enable DTR (default false)")] bool dtr = false,
        [Description("Enable RTS (default false)")] bool rts = false)
    {
        if (string.IsNullOrEmpty(port))
            return JsonSerializer.Serialize(new { success = false, error = "Port name is required (e.g. COM3)" });

        if (_serial.IsOpen)
            return JsonSerializer.Serialize(new { success = true, data = new { message = "Port already open", port = _serial.CurrentPort } });

        var config = new SerialConfig
        {
            PortName = port,
            BaudRate = baudRate,
            DataBits = dataBits,
            StopBits = stopBits,
            Parity = parity,
            DtrEnable = dtr,
            RtsEnable = rts
        };

        if (_serial.Open(config))
            return JsonSerializer.Serialize(new { success = true, data = new { port, baudRate, dataBits } });
        else
            return JsonSerializer.Serialize(new { success = false, error = $"Failed to open port {port}" });
    }

    [McpServerTool, Description("Close the currently open serial port.")]
    public string ClosePort()
    {
        if (_serial.Close())
            return JsonSerializer.Serialize(new { success = true, data = new { message = "Port closed" } });
        else
            return JsonSerializer.Serialize(new { success = false, error = "Failed to close port" });
    }

    // ========== Data Send / Receive ==========

    [McpServerTool, Description("Send data to the serial port. Parameters: data (the text or hex string to send), isHex (if true, data is treated as hex bytes, default false). Returns success status.")]
    public string Send(
        [Description("Data to send (ASCII text or hex string)")] string data,
        [Description("Send as hex bytes (default false)")] bool isHex = false)
    {
        if (string.IsNullOrEmpty(data))
            return JsonSerializer.Serialize(new { success = false, error = "Data cannot be empty" });

        if (_serial.Send(data, isHex))
            return JsonSerializer.Serialize(new
            {
                success = true,
                data = new { sent = data, isHex, byteLength = isHex ? data.Replace(" ", "").Length / 2 : data.Length }
            });
        else
            return JsonSerializer.Serialize(new { success = false, error = "Send failed, port may not be open" });
    }

    [McpServerTool, Description("Read serial port data from the buffer. Parameters: sinceId (return entries with ID > sinceId, default 0), limit (max entries to return, default 100), direction (filter by RX/TX, null for all).")]
    public string ReadData(
        [Description("Return entries with ID greater than this (default 0)")] int sinceId = 0,
        [Description("Maximum number of entries to return (default 100)")] int limit = 100,
        [Description("Filter by direction: RX or TX (null for all)")] string? direction = null)
    {
        List<LogEntry> entries;
        lock (_lock)
        {
            entries = _buffer.Where(e => e.Id > sinceId).ToList();
        }

        if (!string.IsNullOrEmpty(direction))
            entries = entries.Where(e => string.Equals(e.Direction, direction, StringComparison.OrdinalIgnoreCase)).ToList();

        if (limit > 0 && entries.Count > limit)
            entries = entries.Take(limit).ToList();

        return JsonSerializer.Serialize(new
        {
            success = true,
            data = new
            {
                entries,
                count = entries.Count,
                latestId = entries.Count > 0 ? entries[^1].Id : sinceId
            }
        });
    }

    [McpServerTool, Description("Wait for data matching a pattern. Blocks until match or timeout. Parameters: pattern (string to match), timeoutMs (max wait in ms, default 5000, max 60000), matchMode (contains/regex/exact, default contains), matchHex (match against hex data instead of text, default false), direction (RX/TX filter, null for any).")]
    public async Task<string> WaitForResponse(
        [Description("Pattern to match in received data")] string pattern,
        [Description("Timeout in milliseconds (default 5000, max 60000)")] int timeoutMs = 5000,
        [Description("Match mode: contains, regex, or exact (default contains)")] string matchMode = "contains",
        [Description("Match against hex data instead of text (default false)")] bool matchHex = false,
        [Description("Filter direction: RX or TX (null for any)")] string? direction = null)
    {
        if (string.IsNullOrEmpty(pattern))
            return JsonSerializer.Serialize(new { success = false, error = "Pattern is required" });

        var timeout = Math.Clamp(timeoutMs, 100, 60000);
        var entry = await WaitForDataInternalAsync(pattern, matchMode, matchHex, direction, timeout);

        if (entry != null)
            return JsonSerializer.Serialize(new { success = true, data = new { matched = true, entry } });
        else
            return JsonSerializer.Serialize(new { success = true, data = new { matched = false, message = $"Timeout ({timeout}ms), no matching data found" } });
    }

    private async Task<LogEntry?> WaitForDataInternalAsync(string pattern, string matchMode, bool matchHex, string? direction, int timeoutMs)
    {
        var tcs = new TaskCompletionSource<LogEntry?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnData(LogEntry entry)
        {
            if (!string.IsNullOrEmpty(direction) &&
                !string.Equals(entry.Direction, direction, StringComparison.OrdinalIgnoreCase))
                return;

            var target = matchHex ? entry.RawHex : entry.Text;
            if (string.IsNullOrEmpty(target)) return;

            bool matched = matchMode.ToLower() switch
            {
                "exact" => string.Equals(target, pattern, StringComparison.OrdinalIgnoreCase),
                "regex" => System.Text.RegularExpressions.Regex.IsMatch(target, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                _ => target.Contains(pattern)
            };

            if (matched) tcs.TrySetResult(entry);
        }

        _serial.OnDataReceived += OnData;
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var delayTask = Task.Delay(timeoutMs, cts.Token);
            await Task.WhenAny(tcs.Task, delayTask);
            return tcs.Task.IsCompleted ? tcs.Task.Result : null;
        }
        finally
        {
            _serial.OnDataReceived -= OnData;
        }
    }

    [McpServerTool, Description("Clear the data buffer. Parameters: target (rx/tx/all, default all).")]
    public string ClearBuffer(
        [Description("What to clear: rx, tx, or all (default all)")] string? target = null)
    {
        lock (_lock)
        {
            if (target == "rx" || target == null)
                _buffer.RemoveAll(e => e.Direction == "RX");
            if (target == "tx" || target == null)
                _buffer.RemoveAll(e => e.Direction == "TX");
        }
        return JsonSerializer.Serialize(new { success = true, data = new { cleared = target ?? "all" } });
    }

    // ========== Protocol Parser Management ==========

    [McpServerTool, Description("List all available protocol parsers and the currently active one.")]
    public string ListParsers()
    {
        return JsonSerializer.Serialize(new
        {
            success = true,
            data = new
            {
                parsers = _parserManager.AvailableParsers.ToList(),
                active = _parserManager.ActiveParserName
            }
        });
    }

    [McpServerTool, Description("Read the source code of a .csx parser script. Parameters: name (parser name without .csx extension).")]
    public string ReadParser([Description("Parser name (without .csx extension)")] string name)
    {
        var path = Path.Combine(_parserManager.GetParserDir(), name + ".csx");
        if (!File.Exists(path))
            return JsonSerializer.Serialize(new { success = false, error = $"Parser '{name}' not found" });

        var code = File.ReadAllText(path);
        return JsonSerializer.Serialize(new { success = true, data = new { name, code } });
    }

    [McpServerTool, Description("Write or update a .csx protocol parser script. The script uses ScriptGlobals helpers (RawHex, ToUInt16, ToFloat, Crc16, Sum8, Xor8). Must return List<FieldAnnotation>. Parameters: name (parser name), code (C# script code).")]
    public string WriteParser(
        [Description("Parser name (without .csx extension)")] string name,
        [Description("C# script code that returns List<FieldAnnotation>")] string code)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(code))
            return JsonSerializer.Serialize(new { success = false, error = "Both name and code are required" });

        var path = Path.Combine(_parserManager.GetParserDir(), name + ".csx");
        File.WriteAllText(path, code);
        _parserManager.Refresh();

        return JsonSerializer.Serialize(new { success = true, data = new { message = $"Parser '{name}' written", path } });
    }

    [McpServerTool, Description("Activate or deactivate a protocol parser. Pass null or empty to deactivate. Parameters: name (parser name to activate, null to deactivate).")]
    public string ActivateParser([Description("Parser name to activate, or null to deactivate")] string? name)
    {
        if (string.IsNullOrEmpty(name) || name == "(无)")
        {
            _parserManager.Activate(null);
            return JsonSerializer.Serialize(new { success = true, data = new { message = "Parser deactivated" } });
        }

        if (_parserManager.Activate(name))
            return JsonSerializer.Serialize(new { success = true, data = new { message = $"Parser '{name}' activated" } });
        else
            return JsonSerializer.Serialize(new { success = false, error = $"Failed to activate parser: {_parserManager.LastError}" });
    }

    [McpServerTool, Description("Parse raw hex data offline using a protocol parser, without needing an open serial port. Parameters: hex (hex string like 'AA 55 03 ...'), parserName (optional, uses active parser if not specified).")]
    public string ParseRaw(
        [Description("Hex data to parse (e.g. 'AA 55 03 01 19 2E')")] string hex,
        [Description("Parser name to use (null = use currently active parser)")] string? parserName = null)
    {
        if (string.IsNullOrEmpty(hex))
            return JsonSerializer.Serialize(new { success = false, error = "Hex data is required" });

        byte[] data;
        try
        {
            data = Convert.FromHexString(hex.Replace(" ", ""));
        }
        catch
        {
            return JsonSerializer.Serialize(new { success = false, error = "Invalid hex string" });
        }

        // Use specified parser or active parser
        var engine = _parserManager.Engine;
        if (!string.IsNullOrEmpty(parserName) && parserName != _parserManager.ActiveParserName)
        {
            var tempEngine = new ParserEngine();
            var path = Path.Combine(_parserManager.GetParserDir(), parserName + ".csx");
            if (!File.Exists(path))
                return JsonSerializer.Serialize(new { success = false, error = $"Parser '{parserName}' not found" });
            if (!tempEngine.Load(File.ReadAllText(path)))
                return JsonSerializer.Serialize(new { success = false, error = $"Parser load failed: {tempEngine.LastError}" });
            engine = tempEngine;
        }

        if (_parserManager.ActiveParserName == null && string.IsNullOrEmpty(parserName))
            return JsonSerializer.Serialize(new { success = false, error = "No active parser. Use activate_parser first or specify parserName." });

        var fields = engine.Execute(data, DateTime.Now);
        return JsonSerializer.Serialize(new
        {
            success = true,
            data = new
            {
                hex = hex,
                byteCount = data.Length,
                fields,
                fieldCount = fields?.Count ?? 0
            }
        });
    }
}
