using System.ComponentModel;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using ModelContextProtocol.Server;

namespace ACCcom.McpServer.Tools;

[McpServerToolType]
public class SerialTools
{
    private readonly ToolContext _ctx;
    private ISerialService _serial => _ctx.Serial;

    public SerialTools(ToolContext ctx)
    {
        _ctx = ctx;
    }

    [McpServerTool, Description("List all available serial ports on the system.")]
    public Task<string> ListPorts()
    {
        var ports = SerialService.GetAvailablePorts();
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { ports, count = ports.Length } }));
    }

    [McpServerTool, Description("Open a serial port with specified configuration. Parameters: port (required, e.g. COM3), baudRate (default 115200), dataBits (default 8), stopBits (0=None,1=One,2=Two, default 1), parity (0=None,1=Odd,2=Even, default 0), dtr (default false), rts (default false).")]
    public Task<string> OpenPort(
        [Description("Serial port name, e.g. COM3")] string port,
        [Description("Baud rate (default 115200)")] int baudRate = 115200,
        [Description("Data bits (default 8)")] int dataBits = 8,
        [Description("Stop bits: 0=None, 1=One, 2=Two (default 1)")] int stopBits = 1,
        [Description("Parity: 0=None, 1=Odd, 2=Even (default 0)")] int parity = 0,
        [Description("Enable DTR (default false)")] bool dtr = false,
        [Description("Enable RTS (default false)")] bool rts = false)
    {
        if (string.IsNullOrEmpty(port))
            return Task.FromResult(_ctx.RawJson(new { success = false, error = "Port name is required (e.g. COM3)" }));
        if (_serial.IsOpen)
            return Task.FromResult(_ctx.RawJson(new { success = true, data = new { message = "Port already open", port = _serial.CurrentPort } }));

        var config = new SerialConfig { PortName = port, BaudRate = baudRate, DataBits = dataBits, StopBits = stopBits, Parity = parity, DtrEnable = dtr, RtsEnable = rts };
        if (_serial.Open(config))
            return Task.FromResult(_ctx.RawJson(new { success = true, data = new { port, baudRate, dataBits } }));
        return Task.FromResult(_ctx.RawJson(new { success = false, error = $"Failed to open port {port}" }));
    }

    [McpServerTool, Description("Close the currently open serial port.")]
    public Task<string> ClosePort()
    {
        if (_serial.Close())
            return Task.FromResult(_ctx.RawJson(new { success = true, data = new { message = "Port closed" } }));
        return Task.FromResult(_ctx.RawJson(new { success = false, error = "Failed to close port" }));
    }

    [McpServerTool, Description("Send data to the serial port. Parameters: data (the text or hex string to send), isHex (if true, data is treated as hex bytes, default false). Returns success status.")]
    public Task<string> Send(
        [Description("Data to send (ASCII text or hex string)")] string data,
        [Description("Send as hex bytes (default false)")] bool isHex = false)
    {
        if (string.IsNullOrEmpty(data))
            return Task.FromResult(_ctx.RawJson(new { success = false, error = "Data cannot be empty" }));
        if (_serial.Send(data, isHex))
            return Task.FromResult(_ctx.RawJson(new { success = true, data = new { sent = data, isHex, byteLength = isHex ? data.Replace(" ", "").Length / 2 : data.Length } }));
        return Task.FromResult(_ctx.RawJson(new { success = false, error = "Send failed, port may not be open" }));
    }

    [McpServerTool, Description("Read serial port data from the buffer. Parameters: sinceId (return entries with ID > sinceId, default 0), limit (max entries to return, default 100), direction (filter by RX/TX, null for all).")]
    public Task<string> ReadData(
        [Description("Return entries with ID greater than this (default 0)")] int sinceId = 0,
        [Description("Maximum number of entries to return (default 100)")] int limit = 100,
        [Description("Filter by direction: RX or TX (null for all)")] string? direction = null)
    {
        var entries = _ctx.Buffer.GetEntriesSince(sinceId);
        if (!string.IsNullOrEmpty(direction))
            entries = entries.Where(e => string.Equals(e.Direction, direction, StringComparison.OrdinalIgnoreCase)).ToList();
        if (limit > 0 && entries.Count > limit) entries = entries.Take(limit).ToList();
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { entries, count = entries.Count, latestId = entries.Count > 0 ? entries[^1].Id : sinceId } }));
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
        if (string.IsNullOrEmpty(data))
            return _ctx.RawJson(new { success = false, error = "Data cannot be empty" });
        if (string.IsNullOrEmpty(pattern))
            return _ctx.RawJson(new { success = false, error = "Pattern is required" });

        // Register waiter BEFORE sending to avoid race condition
        var timeout = Math.Clamp(timeoutMs, 100, 60000);
        var waiterTask = WaitForDataInternalAsync(pattern, matchMode, matchHex, direction ?? "RX", timeout);

        if (!_serial.Send(data, isHex))
            return _ctx.RawJson(new { success = false, error = "Send failed, port may not be open" });

        var entry = await waiterTask.ConfigureAwait(false);
        if (entry != null)
            return _ctx.RawJson(new { success = true, data = new { sent = data, isHex, matched = true, response = entry } });
        return _ctx.RawJson(new { success = true, data = new { sent = data, isHex, matched = false, message = $"Timeout ({timeout}ms), no matching response" } });
    }

    [McpServerTool, Description("Clear the data buffer. Parameters: target (rx/tx/all, default all).")]
    public Task<string> ClearBuffer(
        [Description("What to clear: rx, tx, or all (default all)")] string? target = null)
    {
        _ctx.Buffer.Clear(target);
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { cleared = target ?? "all" } }));
    }
}
