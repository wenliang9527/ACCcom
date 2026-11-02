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

    [McpServerTool, Description("Open a serial port. Defaults 115200 8N1, no DTR/RTS. tag opens a named multi-port session; omit for the default session. Out-of-range values fail INVALID_CONFIG.")]
    public Task<string> OpenPort(
        [Description("Serial port name (COM3) or TCP endpoint (tcp://host:port)")] string port,
        [Description("Baud rate (default 115200)")] int baudRate = 115200,
        [Description("Data bits 5-8 (default 8)")] int dataBits = 8,
        [Description("Stop bits: 0=None, 1=One, 2=Two (default 1)")] int stopBits = 1,
        [Description("Parity: 0=None, 1=Odd, 2=Even (default 0)")] int parity = 0,
        [Description("Enable DTR (default false)")] bool dtr = false,
        [Description("Enable RTS (default false)")] bool rts = false,
        [Description("Named multi-port session (omit for the default session)")] string? tag = null)
        => _ctx.Guard(() => OpenPortCore(port, baudRate, dataBits, stopBits, parity, dtr, rts, tag));

    private async Task<string> OpenPortCore(
        string port, int baudRate, int dataBits, int stopBits, int parity, bool dtr, bool rts, string? tag)
    {
        if (string.IsNullOrEmpty(port))
            return _ctx.ToolError(ErrorCodes.PortRequired, "Port name is required (e.g. COM3)");

        // Pre-validate the numeric config so out-of-range values fail with a
        // precise INVALID_CONFIG instead of an ArgumentException deep inside
        // SerialPort surfacing as a generic OPEN_FAILED.
        if (baudRate <= 0)
            return _ctx.ToolError(ErrorCodes.InvalidConfig, $"Invalid baudRate {baudRate} (must be positive)");
        if (dataBits is < 5 or > 8)
            return _ctx.ToolError(ErrorCodes.InvalidConfig, $"Invalid dataBits {dataBits} (expected 5-8)");
        if (stopBits is not (0 or 1 or 2))
            return _ctx.ToolError(ErrorCodes.InvalidConfig, $"Invalid stopBits {stopBits} (expected 0=None, 1=One, 2=Two)");
        if (parity is not (0 or 1 or 2))
            return _ctx.ToolError(ErrorCodes.InvalidConfig, $"Invalid parity {parity} (expected 0=None, 1=Odd, 2=Even)");
        // TCP endpoints carry the baud to the bridge as a first-packet command
        // with a firmware-side cap — reject doomed rates up front instead of
        // sending a handshake the bridge silently drops.
        if (baudRate > TcpEndpoint.MaxBridgeBaud && TcpEndpoint.TryParse(port, out _))
            return _ctx.ToolError(ErrorCodes.InvalidConfig, $"TCP endpoint baudRate {baudRate} exceeds bridge cap {TcpEndpoint.MaxBridgeBaud}");

        var config = new SerialConfig { PortName = port, BaudRate = baudRate, DataBits = dataBits, StopBits = stopBits, Parity = parity, DtrEnable = dtr, RtsEnable = rts };

        if (string.IsNullOrEmpty(tag))
        {
            if (_serial.IsOpen)
                // Same shape as a fresh open (port/baudRate/dataBits) plus a
                // message marker, so clients parse one schema. baudRate/dataBits
                // come from the config actually applied at open time, not from
                // this call's arguments.
                return _ctx.RawJson(new
                {
                    success = true,
                    data = new
                    {
                        message = "Port already open",
                        port = _serial.CurrentPort,
                        baudRate = _serial.BaudRate,
                        dataBits = _serial.ActiveConfig?.DataBits ?? dataBits
                    }
                });
            if (await _serial.OpenAsync(config).ConfigureAwait(false))
            {
                NotifyGuiRequested();
                _ctx.TrafficLog.Record(0, "open_port", "SYS", "", port, "");
                // CurrentPort echoes the endpoint the service actually opened
                // (identical to the argument for COM; the normalized
                // tcp://host:port for bare host:port input).
                return _ctx.RawJson(new { success = true, data = new { port = _serial.CurrentPort ?? port, baudRate, dataBits, tag = (string?)null } });
            }
            return _ctx.ToolError(ErrorCodes.OpenFailed, OpenFailureMessage(port, _serial.LastOpenError));
        }

        // Tagged multi-port open.
        var existing = _ctx.MultiPort.GetPort(tag);
        if (existing != null)
            return _ctx.RawJson(new
            {
                success = true,
                data = new
                {
                    message = "Port already open",
                    tag,
                    port = existing.Service.CurrentPort,
                    baudRate = existing.Service.BaudRate,
                    dataBits = existing.Service.ActiveConfig?.DataBits ?? dataBits
                }
            });
        if (await _ctx.MultiPort.OpenPortAsync(tag, config).ConfigureAwait(false))
        {
            NotifyGuiRequested();
            _ctx.TrafficLog.Record(0, "open_port", "SYS", "", port, tag);
            var opened = _ctx.MultiPort.GetPort(tag);
            return _ctx.RawJson(new { success = true, data = new { port = opened?.Service.CurrentPort ?? port, baudRate, dataBits, tag } });
        }
        return _ctx.ToolError(ErrorCodes.OpenFailed, OpenFailureMessage(port, _ctx.MultiPort.LastOpenError, tag));
    }

    /// <summary>Failure text for open_port: the underlying error detail plus an
    /// actionable hint classified from it. A bare "Failed to open port COMx"
    /// cannot distinguish the two everyday cases — the port is held by another
    /// process (GUI session, another agent) vs the device is gone (unplugged,
    /// rebooted out of USB enumeration) — and the agent would burn a round-trip
    /// guessing. LastOpenError null (fakes, null config) still gets the generic
    /// hint, never an empty message.</summary>
    private static string OpenFailureMessage(string port, string? lastError, string? tag = null)
    {
        var subject = string.IsNullOrEmpty(tag) ? $"port {port}" : $"port {port} with tag {tag}";
        if (string.IsNullOrEmpty(lastError))
            return $"Failed to open {subject}. Check the name with list_ports and that the device is connected.";
        if (lastError.Contains("UnauthorizedAccess", StringComparison.Ordinal))
            return $"Failed to open {subject} — access denied ({lastError}). The port is likely held by another process (the ACCcom GUI, a previous session, or a vendor tool); close it there or pick another port.";
        if (lastError.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
            lastError.Contains("FileNotFound", StringComparison.Ordinal))
            return $"Failed to open {subject} — not present ({lastError}). The device may be unplugged, renamed, or a stale USB enumeration after a reboot; re-check with list_ports.";
        return $"Failed to open {subject} ({lastError}). Check the name with list_ports, the driver state, and that the device is connected.";
    }

    [McpServerTool, Description("Close a serial port: the default session, or a tagged multi-port session via tag.")]
    public Task<string> ClosePort(
        [Description("Multi-port session to close (omit for the default session)")] string? tag = null)
        => _ctx.Guard(() => ClosePortCore(tag));

    [McpServerTool, Description("Change the baud rate of an OPEN port without closing it — no DTR/RTS toggle, so devices that reset on reopen stay alive (baud probing). baudRate must be positive (INVALID_CONFIG); no open port fails PORT_NOT_OPEN.")]
    public Task<string> SetBaud(
        [Description("New baud rate (e.g. 9600, 115200)")] int baudRate,
        [Description("Multi-port session tag (omit for the default session)")] string? tag = null)
        => _ctx.Guard(() => SetBaudCore(baudRate, tag));

    private Task<string> SetBaudCore(int baudRate, string? tag)
    {
        if (baudRate <= 0)
            return Task.FromResult(_ctx.ToolError(ErrorCodes.InvalidConfig, $"Invalid baudRate {baudRate} (must be positive)"));
        var service = ServiceFor(tag);
        if (service == null || !service.IsOpen)
            return Task.FromResult(_ctx.ToolError(ErrorCodes.PortNotOpen,
                string.IsNullOrEmpty(tag) ? "Port is not open" : $"Port with tag {tag} is not open"));
        if (!service.SetBaudRate(baudRate))
            return Task.FromResult(_ctx.ToolError(ErrorCodes.InvalidConfig, $"Failed to set baudRate {baudRate}"));
        NotifyGuiRequested();
        _ctx.TrafficLog.Record(0, "set_baud", "SYS", "", baudRate.ToString(), tag ?? "");
        return Task.FromResult(_ctx.RawJson(new
        {
            success = true,
            data = new
            {
                port = service.CurrentPort,
                baudRate,
                // The rate actually applied by the service, not the argument
                // echoed back (mirrors open_port's ActiveConfig discipline).
                appliedBaudRate = service.BaudRate,
                tag = string.IsNullOrEmpty(tag) ? null : tag
            }
        }));
    }

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

    [McpServerTool, Description("Send text or hex data to a port. Returns byteLength, isHex and latestId (read_data sinceId anchor).")]
    public Task<string> Send(
        [Description("Data to send (ASCII text or hex string)")] string data,
        [Description("Send as hex bytes (default false)")] bool isHex = false,
        [Description("Multi-port session tag (omit for the default session)")] string? tag = null)
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
        byte[]? parsedHex = null;
        if (isHex)
        {
            if (!HexHelper.TryHexStringToBytes(data, out var hexBytes))
                return Task.FromResult(_ctx.ToolError(ErrorCodes.InvalidHex, $"Invalid hex: '{data}'"));
            byteLength = hexBytes.Length;
            parsedHex = hexBytes;
        }
        else
        {
            byteLength = HexHelper.CountSendBytes(data, false);
        }

        // Capture the cursor BEFORE the send: a fast device (loopback, bridge)
        // can echo before SendTo returns, and a post-send LastSeq would put
        // that reply BELOW the anchor — the read_data(sinceId) attribution flow
        // would skip it and read "no response". The pre-send cursor makes the
        // anchor race-free: the TX echo row and every arrival after it sit at
        // or above it. No echo of `data` itself: the caller already knows what
        // it sent — echoing a 1KB firmware block back doubled the response.
        var beforeSeq = _ctx.BufferFor(tag).LastSeq;
        if (SendTo(tag, data, isHex))
        {
            NotifyGuiRequested();
            _ctx.TrafficLog.Record(0, "send", "TX", HexForTrafficLog(data, parsedHex), data, tag ?? "");
            return Task.FromResult(_ctx.RawJson(new { success = true, data = new { isHex, byteLength, latestId = beforeSeq, tag = string.IsNullOrEmpty(tag) ? null : tag } }));
        }
        return Task.FromResult(_ctx.ToolError(ErrorCodes.SendFailed, "Send failed, port may not be open"));
    }

    /// <summary>Canonical spaced hex for the traffic mirror. Hex sends log the
    /// decoded bytes re-spaced (raw input spacing varies: "ABCD", "AB CD"),
    /// text sends log their UTF-8 bytes — one format across transports, same
    /// as SerialService/VirtualSerialService TX entries. Internal for
    /// tests.</summary>
    internal static string HexForTrafficLog(string data, byte[]? parsedHex)
        => parsedHex != null
            ? HexHelper.BytesToHexSpaced(parsedHex, 0, parsedHex.Length)
            : HexHelper.BytesToHexSpaced(System.Text.Encoding.UTF8.GetBytes(data), 0, data.Length);

    /// <summary>ANSI CSI escape sequences: ESC [ parameter bytes (0-9 : ; &lt; = &gt; ?)
    /// then a final byte — the color set/reset pairs ESP-class boot logs emit
    /// around every line. Compiled once; matched only when stripAnsi is set.</summary>
    private static readonly System.Text.RegularExpressions.Regex AnsiEscape =
        new("\x1b\\[[0-9;:<=>?]*[\x40-\x7e]", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Read-only copy of a ring entry with a replaced text column —
    /// the shared buffer entry itself must stay untouched (the GUI reads it).</summary>
    private static LogEntry CopyWithText(LogEntry e, string text) => new()
    {
        Id = e.Id,
        Timestamp = e.Timestamp,
        Direction = e.Direction,
        PortTag = e.PortTag,
        RawHex = e.RawHex,
        Text = text,
        ByteCount = e.ByteCount,
        Fields = e.Fields,
        HighlightColor = e.HighlightColor
    };

    [McpServerTool, Description("Read buffered serial data. tail=N returns newest N; cursor mode returns entries after sinceId. Typical flow: send → read_data(sinceId=latestId, waitMs). fields trims columns; maxLength caps size; stripAnsi cleans boot-log colors.")]
    public Task<string> ReadData(
        [Description("Cursor from a previous latestId; returns entries newer than it (default 0)")] int sinceId = 0,
        [Description("Max entries to return (default 100)")] int limit = 100,
        [Description("RX or TX (null for all; other values fail INVALID_DIRECTION)")] string? direction = null,
        [Description("Multi-port session tag (omit for the default session)")] string? tag = null,
        [Description("Newest N entries; overrides sinceId/limit (0 = cursor mode)")] int tail = 0,
        [Description("Truncate each entry's text/hex to N chars, truncated=true when cut (0 = default 2000, max 65536)")] int maxLength = 0,
        [Description("Cursor long-poll: block up to N ms for newer data (default 0, max 60000)")] int waitMs = 0,
        [Description("Comma-separated columns: id,timestamp,direction,portTag,text,hex,truncated (default all; 'text' omits hex)")] string? fields = null,
        [Description("Strip ANSI escape sequences from the text column; hex stays raw (default false)")] bool stripAnsi = false)
        => _ctx.Guard(() => ReadDataCore(sinceId, limit, direction, tag, tail, maxLength, waitMs, fields, stripAnsi));

    private async Task<string> ReadDataCore(
        int sinceId, int limit, string? direction, string? tag, int tail, int maxLength, int waitMs, string? fields, bool stripAnsi)
    {
        var unknownTag = UnknownTagError(tag);
        if (unknownTag != null) return unknownTag;

        if (!McpJson.TryParseFields(fields, out var mask, out var fieldError))
            return _ctx.ToolError(ErrorCodes.InvalidFields, fieldError!);

        // Canonicalize the direction filter before it reaches the buffer: a
        // value like "receive" or a trailing-space "RX " would filter every
        // entry out and read as "no data" instead of "bad argument".
        var dir = ToolArgs.NormalizeDirection(direction, allowSys: false, out var dirError);
        if (dirError != null) return _ctx.ToolError(ErrorCodes.InvalidDirection, dirError);

        var buffer = _ctx.BufferFor(tag);
        List<LogEntry> raw;
        int scannedMaxId;
        if (tail > 0)
        {
            raw = buffer.GetTailEntries(tail, dir, out scannedMaxId);
        }
        else if (waitMs > 0)
        {
            var waited = await buffer.WaitEntriesSinceAsync(sinceId, dir, limit, Math.Clamp(waitMs, 0, 60_000)).ConfigureAwait(false);
            raw = waited.Entries;
            scannedMaxId = waited.ScannedMaxId;
        }
        else
        {
            // Filtering and limit are folded into the buffer's single tail copy.
            raw = buffer.GetEntriesSince(sinceId, dir, limit, out scannedMaxId);
        }

        // stripAnsi=true targets ESP-class boot logs whose CSI color codes wrap
        // every line — the text column then fights regex anchors and wastes
        // context. Entries are copied: they are the same objects the GUI reads
        // from the ring, so mutating Text in place would rewrite the shared
        // buffer. rawHex always keeps the true bytes — this is a render option,
        // never a data change.
        if (stripAnsi)
        {
            for (var i = 0; i < raw.Count; i++)
            {
                var text = raw[i].Text;
                if (text.Length == 0 || !text.Contains('\x1b')) continue;
                raw[i] = CopyWithText(raw[i], AnsiEscape.Replace(text, ""));
            }
        }

        // Lean projection (McpJson.Lean): drops UI-only state, omits empty
        // columns, applies the fields mask and the maxLength truncation
        // so a noisy stream cannot flood the model.
        // Server-side output budget: an omitted maxLength caps every entry at
        // DefaultMaxLength (a full 10k-entry ring with both text and hex could
        // otherwise dump tens of MB into the model context); an explicit value
        // is honored up to MaxMaxLength.
        const int DefaultMaxLength = 2_000;
        const int MaxMaxLength = 65_536;
        var cap = maxLength <= 0 ? DefaultMaxLength : Math.Min(maxLength, MaxMaxLength);
        var lean = new List<LeanEntry>(raw.Count);
        foreach (var e in raw)
            lean.Add(McpJson.Lean(e, cap, mask));

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

    [McpServerTool, Description("Block until received data matches pattern (matchMode: contains/regex/exact) or timeoutMs elapses. Returns the matching entry plus latestId for cursor polling.")]
    public Task<string> WaitForResponse(
        [Description("Pattern to match in received data")] string pattern,
        [Description("Timeout in ms (default 5000, clamped to 100-60000)")] int timeoutMs = 5000,
        [Description("Match mode: contains, regex, or exact (default contains)")] string matchMode = "contains",
        [Description("Match hex data instead of text (default false)")] bool matchHex = false,
        [Description("RX or TX (null for any; other values fail INVALID_DIRECTION)")] string? direction = null,
        [Description("Multi-port session tag (omit for the default session)")] string? tag = null)
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
        // A direction the matcher never agrees with would burn the whole
        // timeout and report "no matching data found" while the frame was
        // already in the buffer — reject it up front instead.
        var dir = ToolArgs.NormalizeDirection(direction, allowSys: false, out var dirError);
        if (dirError != null) return _ctx.ToolError(ErrorCodes.InvalidDirection, dirError);

        var timeout = Math.Clamp(timeoutMs, 100, 60000);
        var entry = await WaitForDataInternalAsync(pattern, matchMode, matchHex, dir, timeout, tag).ConfigureAwait(false);
        var latestId = _ctx.BufferFor(tag).LastSeq;
        if (entry != null)
            return _ctx.RawJson(new { success = true, data = new { matched = true, entry = McpJson.Lean(entry), latestId, tag = string.IsNullOrEmpty(tag) ? null : tag } });
        return _ctx.RawJson(new { success = true, data = new { matched = false, message = $"Timeout ({timeout}ms), no matching data found", latestId, tag = string.IsNullOrEmpty(tag) ? null : tag } });
    }

    internal Task<LogEntry?> WaitForDataInternalAsync(string pattern, string matchMode, bool matchHex, string? direction, int timeoutMs, string? tag = null)
    {
        return _ctx.BufferFor(tag).WaitForMatchAsync(pattern, matchMode, matchHex, direction, timeoutMs);
    }

    [McpServerTool, Description("Wait for quietMs of port silence — detects end of stream after a command; then read_data tail. Returns quiet=true/false plus latestId.")]
    public Task<string> WaitForQuiet(
        [Description("Required continuous silence in ms before declaring quiet (default 200, min 50)")] int quietMs = 200,
        [Description("Max wait in ms (default 5000, clamped to 100-60000)")] int timeoutMs = 5000,
        [Description("Multi-port session tag (omit for the default session)")] string? tag = null)
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

    [McpServerTool, Description("Send data and block until the response matches a pattern. retries=N re-sends after intervalMs. Returns match, attempts and latestId.")]
    public Task<string> SendAndWait(
        [Description("Data to send (ASCII text or hex string)")] string data,
        [Description("Pattern to match in response")] string pattern,
        [Description("Send as hex bytes (default false)")] bool isHex = false,
        [Description("Timeout in ms per attempt (default 5000, clamped to 100-60000)")] int timeoutMs = 5000,
        [Description("Match mode: contains, regex, or exact (default contains)")] string matchMode = "contains",
        [Description("Match hex data instead of text (default false)")] bool matchHex = false,
        [Description("RX or TX (default RX; other values fail INVALID_DIRECTION)")] string? direction = "RX",
        [Description("Max send attempts (default 1 = send once; 1-10)")] int retries = 1,
        [Description("Delay between attempts (default 0; 0-60000)")] int intervalMs = 0,
        [Description("Multi-port session tag (omit for the default session)")] string? tag = null)
        => _ctx.Guard(() => SendAndWaitCore(data, pattern, isHex, timeoutMs, matchMode, matchHex, direction, retries, intervalMs, tag));

    private async Task<string> SendAndWaitCore(
        string data, string pattern, bool isHex, int timeoutMs, string matchMode, bool matchHex,
        string? direction, int retries, int intervalMs, string? tag)
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
        // Normalize before the ?? default: an explicit "" used to slip past it
        // and widen the match to any direction (the tool's own TX echo could
        // then satisfy the wait) — the opposite of read_data's "" semantics.
        var dir = ToolArgs.NormalizeDirection(direction, allowSys: false, out var dirError) ?? "RX";
        if (dirError != null) return _ctx.ToolError(ErrorCodes.InvalidDirection, dirError);
        // Reject the retry knobs up front instead of clamping: a retries=100
        // loop would hold one tool call for minutes and a negative interval
        // would read as "retry silently fast" — both better as precise errors.
        if (retries is < 1 or > 10)
            return _ctx.ToolError(ErrorCodes.InvalidConfig, $"Invalid retries {retries} (expected 1-10)");
        if (intervalMs is < 0 or > 60_000)
            return _ctx.ToolError(ErrorCodes.InvalidConfig, $"Invalid intervalMs {intervalMs} (expected 0-60000)");

        // byteLength mirrors send's contract (strict decoded count for hex,
        // UTF-8 count for text) so send_and_wait responses parse with the
        // same schema as send.
        int byteLength;
        byte[]? parsedHex = null;
        if (isHex)
        {
            if (!HexHelper.TryHexStringToBytes(data, out var hexBytes))
                return _ctx.ToolError(ErrorCodes.InvalidHex, $"Invalid hex: '{data}'");
            byteLength = hexBytes.Length;
            parsedHex = hexBytes;
        }
        else
        {
            byteLength = HexHelper.CountSendBytes(data, false);
        }

        var timeout = Math.Clamp(timeoutMs, 100, 60000);
        LogEntry? entry = null;
        var attempts = 0;
        for (var attempt = 1; attempt <= retries; attempt++)
        {
            attempts = attempt;
            // Register waiter BEFORE sending to avoid race condition
            var waiterTask = WaitForDataInternalAsync(pattern, matchMode, matchHex, dir, timeout, tag);

            if (!SendTo(tag, data, isHex))
                return _ctx.ToolError(ErrorCodes.SendFailed, "Send failed, port may not be open");

            NotifyGuiRequested();
            _ctx.TrafficLog.Record(0, "send_and_wait", "TX", HexForTrafficLog(data, parsedHex), data, tag ?? "");

            entry = await waiterTask.ConfigureAwait(false);
            if (entry != null) break;
            if (intervalMs > 0 && attempt < retries)
                await Task.Delay(intervalMs).ConfigureAwait(false);
        }

        var latestId = _ctx.BufferFor(tag).LastSeq;
        if (entry != null)
            return _ctx.RawJson(new { success = true, data = new { isHex, byteLength, matched = true, attempts, response = McpJson.Lean(entry), latestId, tag = string.IsNullOrEmpty(tag) ? null : tag } });
        return _ctx.RawJson(new { success = true, data = new { isHex, byteLength, matched = false, attempts, message = $"No matching response after {attempts} attempt(s) ({timeout}ms each)", latestId, tag = string.IsNullOrEmpty(tag) ? null : tag } });
    }

    [McpServerTool, Description("Clear the data buffer: rx, tx, or all (default all). Other targets fail INVALID_CONFIG instead of silently keeping the buffer.")]
    public Task<string> ClearBuffer(
        [Description("What to clear: rx, tx, or all (default all)")] string? target = null,
        [Description("Multi-port session tag (omit for the default session)")] string? tag = null)
        => _ctx.Guard(() => ClearBufferCore(target, tag));

    private Task<string> ClearBufferCore(string? target, string? tag)
    {
        // Reject unknown tags BEFORE BufferFor, whose GetOrAdd would otherwise
        // permanently allocate a buffer for a typo'd tag.
        var unknownTag = UnknownTagError(tag);
        if (unknownTag != null) return Task.FromResult(unknownTag);

        // DataBufferService.Clear keeps every entry for an unknown direction
        // value — reporting success for "everything" would make the agent
        // believe stale data was gone and misread old frames as new ones.
        var cleared = ToolArgs.NormalizeClearTarget(target, out var targetError);
        if (targetError != null) return Task.FromResult(_ctx.ToolError(ErrorCodes.InvalidConfig, targetError));

        _ctx.BufferFor(tag).Clear(cleared);
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { cleared, tag = string.IsNullOrEmpty(tag) ? null : tag } }));
    }
}
