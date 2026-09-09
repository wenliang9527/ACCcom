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

    /// <summary>Raised when the AI actually starts serial communication (opens a
    /// port or sends data), so the host can surface the GUI traffic window.
    /// List-only/read-only tool calls deliberately do not raise it.</summary>
    public static event Action? GuiRequested;

    private static void NotifyGuiRequested() => GuiRequested?.Invoke();

    public SerialTools(ToolContext ctx)
    {
        _ctx = ctx;
    }

    // ── Routing helpers ──

    /// <summary>Returns the serial service for a tag: the default single-port
    /// session for the empty tag, otherwise the per-tag multi-port service.
    /// A non-empty tag that was never opened has no service — returns null.</summary>
    private ISerialService? ServiceFor(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return _serial;
        return _ctx.MultiPort.GetPort(tag)?.Service;
    }

    /// <summary>Sends data to a tag's port; returns false if the send fails.</summary>
    private bool SendTo(string? tag, string data, bool isHex)
    {
        if (string.IsNullOrEmpty(tag))
            return _serial.Send(data, isHex);
        return _ctx.MultiPort.SendToPort(tag, data, isHex);
    }

    [McpServerTool, Description("List all available serial ports on the system.")]
    public Task<string> ListPorts()
    {
        var ports = SerialService.GetAvailablePorts();
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { ports, count = ports.Length } }));
    }

    [McpServerTool, Description("List serial ports currently open through this MCP session. Returns tag, port name and baud rate for each open session.")]
    public Task<string> ListOpenPorts()
    {
        var ports = new List<object>();
        // Default single-port session (empty tag) if open.
        if (_serial.IsOpen)
            ports.Add(new { tag = "", port = _serial.CurrentPort, baudRate = _serial.BaudRate });
        foreach (var kv in _ctx.MultiPort.Ports) // snapshot; safe to enumerate
            ports.Add(new { tag = kv.Key, port = kv.Value.Service.CurrentPort, baudRate = kv.Value.Service.BaudRate });
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { ports, count = ports.Count } }));
    }

    [McpServerTool, Description("Open a serial port with specified configuration. Parameters: port (required, e.g. COM3), baudRate (default 115200), dataBits (default 8), stopBits (0=None,1=One,2=Two, default 1), parity (0=None,1=Odd,2=Even, default 0), dtr (default false), rts (default false), tag (optional name to identify this port for multi-port use; omit for the default single-port session).")]
    public Task<string> OpenPort(
        [Description("Serial port name, e.g. COM3")] string port,
        [Description("Baud rate (default 115200)")] int baudRate = 115200,
        [Description("Data bits (default 8)")] int dataBits = 8,
        [Description("Stop bits: 0=None, 1=One, 2=Two (default 1)")] int stopBits = 1,
        [Description("Parity: 0=None, 1=Odd, 2=Even (default 0)")] int parity = 0,
        [Description("Enable DTR (default false)")] bool dtr = false,
        [Description("Enable RTS (default false)")] bool rts = false,
        [Description("Optional tag to name this port for multi-port use (default single-port session if omitted)")] string? tag = null)
    {
        if (string.IsNullOrEmpty(port))
            return Task.FromResult(_ctx.RawJson(new { success = false, error = "Port name is required (e.g. COM3)" }));

        var config = new SerialConfig { PortName = port, BaudRate = baudRate, DataBits = dataBits, StopBits = stopBits, Parity = parity, DtrEnable = dtr, RtsEnable = rts };

        if (string.IsNullOrEmpty(tag))
        {
            if (_serial.IsOpen)
                return Task.FromResult(_ctx.RawJson(new { success = true, data = new { message = "Port already open", port = _serial.CurrentPort } }));
            if (_serial.Open(config))
            {
                NotifyGuiRequested();
                return Task.FromResult(_ctx.RawJson(new { success = true, data = new { port, baudRate, dataBits, tag = "" } }));
            }
            return Task.FromResult(_ctx.RawJson(new { success = false, error = $"Failed to open port {port}" }));
        }

        // Tagged multi-port open.
        var existing = _ctx.MultiPort.GetPort(tag);
        if (existing != null)
            return Task.FromResult(_ctx.RawJson(new { success = true, data = new { message = "Port already open", tag, port = existing.Service.CurrentPort } }));
        if (_ctx.MultiPort.OpenPort(tag, config))
        {
            NotifyGuiRequested();
            return Task.FromResult(_ctx.RawJson(new { success = true, data = new { port, baudRate, dataBits, tag } }));
        }
        return Task.FromResult(_ctx.RawJson(new { success = false, error = $"Failed to open port {port} with tag {tag}" }));
    }

    [McpServerTool, Description("Close a serial port. Parameters: tag (optional name of a multi-port session to close; omit to close the default single-port session).")]
    public Task<string> ClosePort(
        [Description("Optional tag of the multi-port session to close (default single-port session if omitted)")] string? tag = null)
    {
        if (string.IsNullOrEmpty(tag))
        {
            if (_serial.Close())
                return Task.FromResult(_ctx.RawJson(new { success = true, data = new { message = "Port closed" } }));
            return Task.FromResult(_ctx.RawJson(new { success = false, error = "Failed to close port" }));
        }
        if (_ctx.MultiPort.ClosePort(tag))
        {
            // Drop the tag's buffer so a later reopen starts clean and cannot
            // surface stale entries from the previous session.
            _ctx.RemoveBuffer(tag);
            return Task.FromResult(_ctx.RawJson(new { success = true, data = new { message = "Port closed", tag } }));
        }
        return Task.FromResult(_ctx.RawJson(new { success = false, error = $"Failed to close port with tag {tag}" }));
    }

    [McpServerTool, Description("Send data to the serial port. Parameters: data (the text or hex string to send), isHex (if true, data is treated as hex bytes, default false), tag (optional name of a multi-port session; omit for the default single-port session). Returns success status.")]
    public Task<string> Send(
        [Description("Data to send (ASCII text or hex string)")] string data,
        [Description("Send as hex bytes (default false)")] bool isHex = false,
        [Description("Optional tag of the multi-port session to send on (default single-port session if omitted)")] string? tag = null)
    {
        if (string.IsNullOrEmpty(data))
            return Task.FromResult(_ctx.RawJson(new { success = false, error = "Data cannot be empty" }));
        var service = ServiceFor(tag);
        if (service == null)
            return Task.FromResult(_ctx.RawJson(new { success = false, error = $"Port with tag {tag} is not open" }));
        if (SendTo(tag, data, isHex))
        {
            NotifyGuiRequested();
            _ctx.TrafficLog.Record(0, "send", "TX", isHex ? data : HexHelper.BytesToHexSpaced(System.Text.Encoding.UTF8.GetBytes(data), 0, data.Length), data, tag ?? "");
            return Task.FromResult(_ctx.RawJson(new { success = true, data = new { sent = data, isHex, byteLength = isHex ? HexHelper.CountHexBytes(data) : data.Length, tag = tag ?? "" } }));
        }
        return Task.FromResult(_ctx.RawJson(new { success = false, error = "Send failed, port may not be open" }));
    }

    [McpServerTool, Description("Read serial port data from the buffer. Parameters: sinceId (return entries with ID > sinceId, default 0), limit (max entries to return, default 100), direction (filter by RX/TX, null for all), tag (optional name of a multi-port session; omit for the default single-port session).")]
    public Task<string> ReadData(
        [Description("Return entries with ID greater than this (default 0)")] int sinceId = 0,
        [Description("Maximum number of entries to return (default 100)")] int limit = 100,
        [Description("Filter by direction: RX or TX (null for all)")] string? direction = null,
        [Description("Optional tag of the multi-port session to read (default single-port session if omitted)")] string? tag = null)
    {
        // Filtering and limit are folded into the buffer's single tail copy.
        var entries = _ctx.BufferFor(tag).GetEntriesSince(sinceId, direction, limit);
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { entries, count = entries.Count, latestId = entries.Count > 0 ? entries[^1].Id : sinceId, tag = tag ?? "" } }));
    }

    [McpServerTool, Description("Wait for data matching a pattern. Blocks until match or timeout. Parameters: pattern (string to match), timeoutMs (max wait in ms, default 5000, max 60000), matchMode (contains/regex/exact, default contains), matchHex (match against hex data instead of text, default false), direction (RX/TX filter, null for any), tag (optional name of a multi-port session; omit for the default single-port session).")]
    public async Task<string> WaitForResponse(
        [Description("Pattern to match in received data")] string pattern,
        [Description("Timeout in milliseconds (default 5000, max 60000)")] int timeoutMs = 5000,
        [Description("Match mode: contains, regex, or exact (default contains)")] string matchMode = "contains",
        [Description("Match against hex data instead of text (default false)")] bool matchHex = false,
        [Description("Filter direction: RX or TX (null for any)")] string? direction = null,
        [Description("Optional tag of the multi-port session to wait on (default single-port session if omitted)")] string? tag = null)
    {
        if (string.IsNullOrEmpty(pattern))
            return _ctx.RawJson(new { success = false, error = "Pattern is required" });
        var timeout = Math.Clamp(timeoutMs, 100, 60000);
        var entry = await WaitForDataInternalAsync(pattern, matchMode, matchHex, direction, timeout, tag).ConfigureAwait(false);
        if (entry != null)
            return _ctx.RawJson(new { success = true, data = new { matched = true, entry, tag = tag ?? "" } });
        return _ctx.RawJson(new { success = true, data = new { matched = false, message = $"Timeout ({timeout}ms), no matching data found", tag = tag ?? "" } });
    }

    internal Task<LogEntry?> WaitForDataInternalAsync(string pattern, string matchMode, bool matchHex, string? direction, int timeoutMs, string? tag = null)
    {
        return _ctx.BufferFor(tag).WaitForMatchAsync(pattern, matchMode, matchHex, direction, timeoutMs);
    }

    [McpServerTool, Description("Send data to serial port and wait for a matching response. Combines send + wait_for_response in one call. Parameters: data (text or hex to send), pattern (response pattern to match), isHex (default false), timeoutMs (default 5000, max 60000), matchMode (contains/regex/exact, default contains), matchHex (match against hex data instead of text, default false), direction (RX/TX filter, default RX), tag (optional name of a multi-port session; omit for the default single-port session).")]
    public async Task<string> SendAndWait(
        [Description("Data to send (ASCII text or hex string)")] string data,
        [Description("Pattern to match in response")] string pattern,
        [Description("Send as hex bytes (default false)")] bool isHex = false,
        [Description("Timeout in milliseconds (default 5000, max 60000)")] int timeoutMs = 5000,
        [Description("Match mode: contains, regex, or exact (default contains)")] string matchMode = "contains",
        [Description("Match against hex data instead of text (default false)")] bool matchHex = false,
        [Description("Filter direction: RX or TX (default RX)")] string? direction = "RX",
        [Description("Optional tag of the multi-port session to use (default single-port session if omitted)")] string? tag = null)
    {
        if (string.IsNullOrEmpty(data))
            return _ctx.RawJson(new { success = false, error = "Data cannot be empty" });
        if (string.IsNullOrEmpty(pattern))
            return _ctx.RawJson(new { success = false, error = "Pattern is required" });
        var service = ServiceFor(tag);
        if (service == null)
            return _ctx.RawJson(new { success = false, error = $"Port with tag {tag} is not open" });

        // Register waiter BEFORE sending to avoid race condition
        var timeout = Math.Clamp(timeoutMs, 100, 60000);
        var waiterTask = WaitForDataInternalAsync(pattern, matchMode, matchHex, direction ?? "RX", timeout, tag);

        if (!SendTo(tag, data, isHex))
            return _ctx.RawJson(new { success = false, error = "Send failed, port may not be open" });

        NotifyGuiRequested();
        _ctx.TrafficLog.Record(0, "send_and_wait", "TX", isHex ? data : HexHelper.BytesToHexSpaced(System.Text.Encoding.UTF8.GetBytes(data), 0, data.Length), data, tag ?? "");

        var entry = await waiterTask.ConfigureAwait(false);
        if (entry != null)
            return _ctx.RawJson(new { success = true, data = new { sent = data, isHex, matched = true, response = entry, tag = tag ?? "" } });
        return _ctx.RawJson(new { success = true, data = new { sent = data, isHex, matched = false, message = $"Timeout ({timeout}ms), no matching response", tag = tag ?? "" } });
    }

    [McpServerTool, Description("Clear the data buffer. Parameters: target (rx/tx/all, default all), tag (optional name of a multi-port session; omit for the default single-port session).")]
    public Task<string> ClearBuffer(
        [Description("What to clear: rx, tx, or all (default all)")] string? target = null,
        [Description("Optional tag of the multi-port session to clear (default single-port session if omitted)")] string? tag = null)
    {
        _ctx.BufferFor(tag).Clear(target);
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { cleared = target ?? "all", tag = tag ?? "" } }));
    }
}