using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.McpServer.Tools;

/// <summary>
/// Shared context for all MCP tool classes.
///
/// Holds two routing planes:
/// - The default single-port session (<see cref="Serial"/>/<see cref="Buffer"/>),
///   used by tools called without a tag — exactly the pre-multi-port behavior.
/// - A <see cref="MultiPort"/> service for named tags; each opened tag owns an
///   independent ISerialService and its own per-tag buffer
///   (<see cref="BufferFor"/>), so concurrent ports stay isolated.
///
/// Incoming data is routed by <c>LogEntry.PortTag</c>: empty tag → default
/// buffer, non-empty tag → that tag's buffer. The shared traffic log records
/// the tag so the GUI can show which port a line belongs to.
/// </summary>
public class ToolContext
{
    /// <summary>Default single-port session (empty tag). Back-compat surface.</summary>
    public ISerialService Serial { get; }

    /// <summary>Default single-port buffer. Back-compat surface.</summary>
    public DataBufferService Buffer { get; } = new();

    /// <summary>Multi-port service; each non-empty tag owns an independent ISerialService.</summary>
    public MultiPortService MultiPort { get; }

    /// <summary>Per-tag data buffers, created on demand and isolated from each
    /// other. ConcurrentDictionary so the per-packet BufferFor lookup on the
    /// multi-port RX path takes no lock (GetOrAdd is atomic — same semantics
    /// as the old lock(TryGetValue/Add) sequence).</summary>
    public ConcurrentDictionary<string, DataBufferService> Buffers { get; } = new();

    /// <summary>Traffic mirror sink. Defaults to the process-wide shared JSONL
    /// writer; tests swap in a temp-file instance before triggering traffic so
    /// unit runs never pollute the real mcp-traffic.jsonl, and can assert on
    /// what was recorded (settable for DI-free construction).</summary>
    public McpTrafficLog TrafficLog { get; set; } = McpTrafficLog.Shared;

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        // Sparse-response policy: null columns never reach the wire. Callers
        // hand nulls (e.g. an empty tag echo) to get the column omitted —
        // matching read_data's source-gen output, which null-omits too.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ToolContext(MultiPortService multiPort, ISerialService defaultSerial)
    {
        ArgumentNullException.ThrowIfNull(multiPort);
        ArgumentNullException.ThrowIfNull(defaultSerial);
        MultiPort = multiPort;
        Serial = defaultSerial;

        Serial.OnDataReceived += entry =>
        {
            Buffer.AddEntry(entry);
            RecordReceived(entry, "");
        };

        multiPort.OnDataReceived += entry =>
        {
            var tag = entry.PortTag ?? "";
            var buffer = BufferFor(tag);
            buffer.AddEntry(entry);
            RecordReceived(entry, tag);
        };
    }

    /// <summary>Mirrors a received entry into the traffic log. TX entries are
    /// deliberately skipped: they always originate from a send tool, which
    /// records them itself with the real tool name (send / send_and_wait) —
    /// recording them here as well wrote a duplicate line mislabeled
    /// tool="rx" for every TX.</summary>
    private void RecordReceived(LogEntry entry, string tag)
    {
        if (string.Equals(entry.Direction, "TX", StringComparison.Ordinal)) return;
        TrafficLog.Record(entry.Id, "rx", entry.Direction, entry.RawHex ?? "", entry.Text ?? "", tag);
    }

    /// <summary>Returns the buffer for a tag: the default single-port buffer for
    /// the empty tag, otherwise the per-tag buffer (created on demand).</summary>
    public DataBufferService BufferFor(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return Buffer;
        return Buffers.GetOrAdd(tag, static _ => new DataBufferService());
    }

    /// <summary>Drops a tag's per-tag buffer when its port is closed, so a later
    /// reopen of the same tag starts with a clean buffer and does not surface
    /// stale entries from the previous session.</summary>
    public void RemoveBuffer(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return;
        Buffers.TryRemove(tag, out _);
    }

    public string RawJson(object obj) =>
        JsonSerializer.Serialize(obj, JsonOpts);

    /// <summary>Standard failure envelope:
    /// {"success":false,"error":{"code":"STABLE_CODE","message":"human text"}}.
    /// <paramref name="code"/> must be one of <see cref="ErrorCodes"/>.</summary>
    public string ToolError(string code, string message) =>
        RawJson(new { success = false, error = new { code, message } });
}
