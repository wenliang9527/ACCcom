using System.ComponentModel;
using System.Text.RegularExpressions;
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

    /// <summary>Failure envelope for a non-empty tag that has no open port, or
    /// null when the tag resolves. The empty tag is the default session and is
    /// always addressable — reads on it are simply empty until a port opens.
    /// Without this check a typo'd tag silently returned empty data (and
    /// clear_buffer allocated an orphan buffer), which an agent reads as
    /// "no data" instead of "wrong tag".</summary>
    private string? UnknownTagError(string? tag) =>
        string.IsNullOrEmpty(tag) || ServiceFor(tag) != null
            ? null
            : _ctx.ToolError(ErrorCodes.PortNotOpen, $"No open port with tag '{tag}'");

    /// <summary>Returns an error message when matchMode is not contains/exact/regex
    /// or a regex pattern cannot compile; null when the arguments are valid.
    /// DataBufferService.WaitForMatchAsync silently degrades both cases (unknown
    /// mode → contains, bad regex → never matches), so wait tools would report a
    /// misleading timeout — reject them up front instead.</summary>
    private static string? PatternArgError(string? matchMode, string pattern)
    {
        var mode = (matchMode ?? "").Trim();
        if (mode.Length == 0)
            return null; // same leniency as the matcher: empty degrades to contains
        var isContains = mode.Equals("contains", StringComparison.OrdinalIgnoreCase);
        var isExact = mode.Equals("exact", StringComparison.OrdinalIgnoreCase);
        var isRegex = mode.Equals("regex", StringComparison.OrdinalIgnoreCase);
        if (!isContains && !isExact && !isRegex)
            return $"Unknown matchMode '{matchMode}' (expected contains, exact, or regex)";
        if (isRegex)
        {
            // Mirror the matcher's compile options; Compilation is irrelevant
            // to whether the pattern is syntactically valid.
            try { _ = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)); }
            catch (ArgumentException ex) { return $"Invalid regex pattern: {ex.Message}"; }
        }
        return null;
    }

    [McpServerTool, Description("List all available serial ports on the system.")]
    public Task<string> ListPorts() => _ctx.Guard(ListPortsCore);

    private Task<string> ListPortsCore()
    {
        var ports = SerialService.GetAvailablePorts();
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { ports, count = ports.Length } }));
    }

    [McpServerTool, Description("List serial ports currently open in this MCP session (tag, port, baudRate).")]
    public Task<string> ListOpenPorts() => _ctx.Guard(ListOpenPortsCore);

    private Task<string> ListOpenPortsCore()
    {
        var ports = new List<object>();
        // Default single-port session (empty tag) if open.
        if (_serial.IsOpen)
            ports.Add(new { tag = "", port = _serial.CurrentPort, baudRate = _serial.BaudRate });
        foreach (var kv in _ctx.MultiPort.Ports) // snapshot; safe to enumerate
            ports.Add(new { tag = kv.Key, port = kv.Value.Service.CurrentPort, baudRate = kv.Value.Service.BaudRate });
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { ports, count = ports.Count } }));
    }

    [McpServerTool, Description("Open a serial port. Defaults: 115200 8N1, no DTR/RTS. Pass tag to open a named multi-port session; omit it for the default session.")]
    public Task<string> OpenPort(
        [Description("Serial port name, e.g. COM3")] string port,
        [Description("Baud rate (default 115200)")] int baudRate = 115200,
        [Description("Data bits (default 8)")] int dataBits = 8,
        [Description("Stop bits: 0=None, 1=One, 2=Two (default 1)")] int stopBits = 1,
        [Description("Parity: 0=None, 1=Odd, 2=Even (default 0)")] int parity = 0,
        [Description("Enable DTR (default false)")] bool dtr = false,
        [Description("Enable RTS (default false)")] bool rts = false,
        [Description("Optional tag to name this port for multi-port use (default single-port session if omitted)")] string? tag = null)
        => _ctx.Guard(() => OpenPortCore(port, baudRate, dataBits, stopBits, parity, dtr, rts, tag));

    private Task<string> OpenPortCore(
        string port, int baudRate, int dataBits, int stopBits, int parity, bool dtr, bool rts, string? tag)
    {
        if (string.IsNullOrEmpty(port))
            return Task.FromResult(_ctx.ToolError(ErrorCodes.PortRequired, "Port name is required (e.g. COM3)"));

        var config = new SerialConfig { PortName = port, BaudRate = baudRate, DataBits = dataBits, StopBits = stopBits, Parity = parity, DtrEnable = dtr, RtsEnable = rts };

        if (string.IsNullOrEmpty(tag))
        {
            if (_serial.IsOpen)
                return Task.FromResult(_ctx.RawJson(new { success = true, data = new { message = "Port already open", port = _serial.CurrentPort } }));
            if (_serial.Open(config))
            {
                NotifyGuiRequested();
                _ctx.TrafficLog.Record(0, "open_port", "SYS", "", port, "");
                return Task.FromResult(_ctx.RawJson(new { success = true, data = new { port, baudRate, dataBits, tag = (string?)null } }));
            }
            return Task.FromResult(_ctx.ToolError(ErrorCodes.OpenFailed, $"Failed to open port {port}"));
        }

        // Tagged multi-port open.
        var existing = _ctx.MultiPort.GetPort(tag);
        if (existing != null)
            return Task.FromResult(_ctx.RawJson(new { success = true, data = new { message = "Port already open", tag, port = existing.Service.CurrentPort } }));
        if (_ctx.MultiPort.OpenPort(tag, config))
        {
            NotifyGuiRequested();
            _ctx.TrafficLog.Record(0, "open_port", "SYS", "", port, tag);
            return Task.FromResult(_ctx.RawJson(new { success = true, data = new { port, baudRate, dataBits, tag } }));
        }
        return Task.FromResult(_ctx.ToolError(ErrorCodes.OpenFailed, $"Failed to open port {port} with tag {tag}"));
    }

    [McpServerTool, Description("Close a serial port: the default session, or a tagged multi-port session via tag.")]
    public Task<string> ClosePort(
        [Description("Optional tag of the multi-port session to close (default single-port session if omitted)")] string? tag = null)
        => _ctx.Guard(() => ClosePortCore(tag));

    private Task<string> ClosePortCore(string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            // Honest close: a port that was never open must not report success.
            // SerialService.Close() returns true unconditionally, so the IsOpen
            // check is what keeps "nothing to close" distinguishable.
            if (!_serial.IsOpen)
                return Task.FromResult(_ctx.ToolError(ErrorCodes.PortNotOpen, "Port is not open"));
            _serial.Close();
            _ctx.TrafficLog.Record(0, "close_port", "SYS", "", "", "");
            return Task.FromResult(_ctx.RawJson(new { success = true, data = new { message = "Port closed" } }));
        }
        if (_ctx.MultiPort.GetPort(tag) == null)
            return Task.FromResult(_ctx.ToolError(ErrorCodes.PortNotOpen, $"No open port with tag '{tag}'"));
        _ctx.MultiPort.ClosePort(tag);
        _ctx.TrafficLog.Record(0, "close_port", "SYS", "", "", tag);
        // Drop the tag's buffer so a later reopen starts clean and cannot
        // surface stale entries from the previous session.
        _ctx.RemoveBuffer(tag);
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { message = "Port closed", tag } }));
    }

    [McpServerTool, Description("Send text or hex data to a port. Returns byteLength and isHex of what was sent.")]
    public Task<string> Send(
        [Description("Data to send (ASCII text or hex string)")] string data,
        [Description("Send as hex bytes (default false)")] bool isHex = false,
        [Description("Optional tag of the multi-port session to send on (default single-port session if omitted)")] string? tag = null)
        => _ctx.Guard(() => SendCore(data, isHex, tag));

    private Task<string> SendCore(string data, bool isHex, string? tag)
    {
        if (string.IsNullOrEmpty(data))
            return Task.FromResult(_ctx.ToolError(ErrorCodes.EmptyData, "Data cannot be empty"));
        var service = ServiceFor(tag);
        if (service == null)
            return Task.FromResult(_ctx.ToolError(ErrorCodes.PortNotOpen, $"Port with tag {tag} is not open"));

        // Validate hex up front so a malformed string fails with a precise
        // message instead of a generic "Send failed" from the port's own
        // conversion. byteLength is then the actual decoded byte count; for
        // text mode it is the UTF-8 byte count (a char count would under-report
        // multi-byte characters like CJK).
        int byteLength;
        if (isHex)
        {
            if (!HexHelper.TryHexStringToBytes(data, out var hexBytes))
                return Task.FromResult(_ctx.ToolError(ErrorCodes.InvalidHex, $"Invalid hex: '{data}'"));
            byteLength = hexBytes.Length;
        }
        else
        {
            byteLength = HexHelper.CountSendBytes(data, false);
        }

        if (SendTo(tag, data, isHex))
        {
            NotifyGuiRequested();
            var txBytes = System.Text.Encoding.UTF8.GetBytes(data);
            _ctx.TrafficLog.Record(0, "send", "TX", isHex ? data : HexHelper.BytesToHexSpaced(txBytes, 0, txBytes.Length), data, tag ?? "");
            // No echo of `data`: the caller already knows what it sent — echoing
            // a 1KB firmware block back doubled the response for nothing.
            return Task.FromResult(_ctx.RawJson(new { success = true, data = new { isHex, byteLength, tag = string.IsNullOrEmpty(tag) ? null : tag } }));
        }
        return Task.FromResult(_ctx.ToolError(ErrorCodes.SendFailed, "Send failed, port may not be open"));
    }

    [McpServerTool, Description("Read buffered serial data. tail=N returns the newest N entries; cursor mode returns entries after sinceId (echo the previous latestId, add waitMs to long-poll until new data arrives). Typical flow: send → wait_for_quiet → read_data tail. Use fields to trim columns and maxLength to cap entry size.")]
    public Task<string> ReadData(
        [Description("Cursor from a previous call's latestId; entries newer than it are returned (default 0)")] int sinceId = 0,
        [Description("Maximum number of entries to return (default 100)")] int limit = 100,
        [Description("Filter by direction: RX or TX (null for all)")] string? direction = null,
        [Description("Optional tag of the multi-port session to read (default single-port session if omitted)")] string? tag = null,
        [Description("Return the newest N entries in arrival order; takes precedence over sinceId/limit (default 0 = cursor mode)")] int tail = 0,
        [Description("Truncate text and hex of each entry to this many characters, marking truncated=true (default 0 = no truncation)")] int maxLength = 0,
        [Description("Cursor mode only: long-poll — block up to this many ms for data newer than sinceId, returning immediately when it arrives (default 0 = non-blocking, max 60000)")] int waitMs = 0,
        [Description("Comma-separated columns to include: id,timestamp,direction,portTag,text,hex,truncated (default all; e.g. 'text' omits hex — the largest column on binary streams)")] string? fields = null)
        => _ctx.Guard(() => ReadDataCore(sinceId, limit, direction, tag, tail, maxLength, waitMs, fields));

    private async Task<string> ReadDataCore(
        int sinceId, int limit, string? direction, string? tag, int tail, int maxLength, int waitMs, string? fields)
    {
        var unknownTag = UnknownTagError(tag);
        if (unknownTag != null) return unknownTag;

        if (!McpJson.TryParseFields(fields, out var mask, out var fieldError))
            return _ctx.ToolError(ErrorCodes.InvalidFields, fieldError!);

        var buffer = _ctx.BufferFor(tag);
        List<LogEntry> raw;
        int scannedMaxId;
        if (tail > 0)
        {
            raw = buffer.GetTailEntries(tail, direction, out scannedMaxId);
        }
        else if (waitMs > 0)
        {
            var waited = await buffer.WaitEntriesSinceAsync(sinceId, direction, limit, Math.Clamp(waitMs, 0, 60_000)).ConfigureAwait(false);
            raw = waited.Entries;
            scannedMaxId = waited.ScannedMaxId;
        }
        else
        {
            // Filtering and limit are folded into the buffer's single tail copy.
            raw = buffer.GetEntriesSince(sinceId, direction, limit, out scannedMaxId);
        }

        // Lean projection (McpJson.Lean): drops UI-only state, omits empty
        // columns, applies the fields mask and the opt-in maxLength truncation
        // so a noisy stream cannot flood the model.
        var lean = new List<LeanEntry>(raw.Count);
        foreach (var e in raw)
            lean.Add(McpJson.Lean(e, maxLength, mask));

        // latestId is the buffer's arrival-sequence cursor, NOT an Entry.Id:
        // entry ids come from independent per-direction/per-port counters and
        // are non-monotonic in arrival order, so mixing them into the cursor
        // (or taking the last returned entry's Id) skips entries whose counter
        // lagged behind. scannedMaxId also covers filter-skipped tails.
        return McpJson.Serialize(new ReadDataResponse
        {
            success = true,
            data = new ReadDataBody
            {
                entries = lean,
                count = lean.Count,
                latestId = scannedMaxId,
                tag = string.IsNullOrEmpty(tag) ? null : tag
            }
        });
    }

    [McpServerTool, Description("Block until received data matches pattern (matchMode: contains/regex/exact) or timeoutMs elapses. Returns the matching entry plus latestId to continue cursor polling from.")]
    public Task<string> WaitForResponse(
        [Description("Pattern to match in received data")] string pattern,
        [Description("Timeout in milliseconds (default 5000, max 60000)")] int timeoutMs = 5000,
        [Description("Match mode: contains, regex, or exact (default contains)")] string matchMode = "contains",
        [Description("Match against hex data instead of text (default false)")] bool matchHex = false,
        [Description("Filter direction: RX or TX (null for any)")] string? direction = null,
        [Description("Optional tag of the multi-port session to wait on (default single-port session if omitted)")] string? tag = null)
        => _ctx.Guard(() => WaitForResponseCore(pattern, timeoutMs, matchMode, matchHex, direction, tag));

    private async Task<string> WaitForResponseCore(
        string pattern, int timeoutMs, string matchMode, bool matchHex, string? direction, string? tag)
    {
        if (string.IsNullOrEmpty(pattern))
            return _ctx.ToolError(ErrorCodes.PatternRequired, "Pattern is required");
        var unknownTag = UnknownTagError(tag);
        if (unknownTag != null) return unknownTag;
        var patternError = PatternArgError(matchMode, pattern);
        if (patternError != null)
            return _ctx.ToolError(ErrorCodes.InvalidPattern, patternError);

        var timeout = Math.Clamp(timeoutMs, 100, 60000);
        var entry = await WaitForDataInternalAsync(pattern, matchMode, matchHex, direction, timeout, tag).ConfigureAwait(false);
        var latestId = _ctx.BufferFor(tag).LastSeq;
        if (entry != null)
            return _ctx.RawJson(new { success = true, data = new { matched = true, entry = McpJson.Lean(entry), latestId, tag = string.IsNullOrEmpty(tag) ? null : tag } });
        return _ctx.RawJson(new { success = true, data = new { matched = false, message = $"Timeout ({timeout}ms), no matching data found", latestId, tag = string.IsNullOrEmpty(tag) ? null : tag } });
    }

    internal Task<LogEntry?> WaitForDataInternalAsync(string pattern, string matchMode, bool matchHex, string? direction, int timeoutMs, string? tag = null)
    {
        return _ctx.BufferFor(tag).WaitForMatchAsync(pattern, matchMode, matchHex, direction, timeoutMs);
    }

    [McpServerTool, Description("Wait for quietMs of port silence — use after a command to detect end of stream, then read_data tail. Returns quiet=true/false plus latestId.")]
    public Task<string> WaitForQuiet(
        [Description("Required continuous silence in ms before declaring quiet (default 200, min 50)")] int quietMs = 200,
        [Description("Max wait in ms (default 5000, max 60000)")] int timeoutMs = 5000,
        [Description("Optional tag of the multi-port session to watch (default single-port session if omitted)")] string? tag = null)
        => _ctx.Guard(() => WaitForQuietCore(quietMs, timeoutMs, tag));

    private async Task<string> WaitForQuietCore(int quietMs, int timeoutMs, string? tag)
    {
        var unknownTag = UnknownTagError(tag);
        if (unknownTag != null) return unknownTag;

        var quiet = Math.Max(50, quietMs);
        var timeout = Math.Clamp(timeoutMs, 100, 60000);
        var buffer = _ctx.BufferFor(tag);
        var isQuiet = await buffer.WaitForQuietAsync(quiet, timeout).ConfigureAwait(false);
        var latestId = buffer.LastSeq;
        if (isQuiet)
            return _ctx.RawJson(new { success = true, data = new { quiet = true, quietMs = quiet, latestId, tag = string.IsNullOrEmpty(tag) ? null : tag } });
        return _ctx.RawJson(new { success = true, data = new { quiet = false, quietMs = quiet, message = $"Not quiet: {timeout}ms elapsed without {quietMs}ms of silence", latestId, tag = string.IsNullOrEmpty(tag) ? null : tag } });
    }

    [McpServerTool, Description("Send data and block until the response matches a pattern (combines send + wait_for_response). Returns match plus latestId to continue cursor polling from.")]
    public Task<string> SendAndWait(
        [Description("Data to send (ASCII text or hex string)")] string data,
        [Description("Pattern to match in response")] string pattern,
        [Description("Send as hex bytes (default false)")] bool isHex = false,
        [Description("Timeout in milliseconds (default 5000, max 60000)")] int timeoutMs = 5000,
        [Description("Match mode: contains, regex, or exact (default contains)")] string matchMode = "contains",
        [Description("Match against hex data instead of text (default false)")] bool matchHex = false,
        [Description("Filter direction: RX or TX (default RX)")] string? direction = "RX",
        [Description("Optional tag of the multi-port session to use (default single-port session if omitted)")] string? tag = null)
        => _ctx.Guard(() => SendAndWaitCore(data, pattern, isHex, timeoutMs, matchMode, matchHex, direction, tag));

    private async Task<string> SendAndWaitCore(
        string data, string pattern, bool isHex, int timeoutMs, string matchMode, bool matchHex, string? direction, string? tag)
    {
        if (string.IsNullOrEmpty(data))
            return _ctx.ToolError(ErrorCodes.EmptyData, "Data cannot be empty");
        if (string.IsNullOrEmpty(pattern))
            return _ctx.ToolError(ErrorCodes.PatternRequired, "Pattern is required");
        var unknownTag = UnknownTagError(tag);
        if (unknownTag != null) return unknownTag;
        var patternError = PatternArgError(matchMode, pattern);
        if (patternError != null)
            return _ctx.ToolError(ErrorCodes.InvalidPattern, patternError);
        if (isHex && !HexHelper.TryHexStringToBytes(data, out _))
            return _ctx.ToolError(ErrorCodes.InvalidHex, $"Invalid hex: '{data}'");

        // Register waiter BEFORE sending to avoid race condition
        var timeout = Math.Clamp(timeoutMs, 100, 60000);
        var waiterTask = WaitForDataInternalAsync(pattern, matchMode, matchHex, direction ?? "RX", timeout, tag);

        if (!SendTo(tag, data, isHex))
            return _ctx.ToolError(ErrorCodes.SendFailed, "Send failed, port may not be open");

        NotifyGuiRequested();
        var txBytes = System.Text.Encoding.UTF8.GetBytes(data);
        _ctx.TrafficLog.Record(0, "send_and_wait", "TX", isHex ? data : HexHelper.BytesToHexSpaced(txBytes, 0, txBytes.Length), data, tag ?? "");

        var entry = await waiterTask.ConfigureAwait(false);
        var latestId = _ctx.BufferFor(tag).LastSeq;
        if (entry != null)
            return _ctx.RawJson(new { success = true, data = new { isHex, matched = true, response = McpJson.Lean(entry), latestId, tag = string.IsNullOrEmpty(tag) ? null : tag } });
        return _ctx.RawJson(new { success = true, data = new { isHex, matched = false, message = $"Timeout ({timeout}ms), no matching response", latestId, tag = string.IsNullOrEmpty(tag) ? null : tag } });
    }

    [McpServerTool, Description("Clear the data buffer: rx, tx, or all (default all).")]
    public Task<string> ClearBuffer(
        [Description("What to clear: rx, tx, or all (default all)")] string? target = null,
        [Description("Optional tag of the multi-port session to clear (default single-port session if omitted)")] string? tag = null)
        => _ctx.Guard(() => ClearBufferCore(target, tag));

    private Task<string> ClearBufferCore(string? target, string? tag)
    {
        // Reject unknown tags BEFORE BufferFor, whose GetOrAdd would otherwise
        // permanently allocate a buffer for a typo'd tag.
        var unknownTag = UnknownTagError(tag);
        if (unknownTag != null) return Task.FromResult(unknownTag);

        _ctx.BufferFor(tag).Clear(target);
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { cleared = target ?? "all", tag = string.IsNullOrEmpty(tag) ? null : tag } }));
    }
}
