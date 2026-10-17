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
    /// multi-port RX path takes no lock. Each entry costs ~120 KB
    /// (LogEntry?[10000] + int[10000]), so the set is capped: creating a tag
    /// beyond <see cref="MaxTrackedTags"/> evicts the oldest tag whose port is
    /// no longer open (falling back to the oldest overall). Every close path —
    /// MultiPortService.ClosePort, CloseAll, and a serial-level disconnect —
    /// releases its buffer through <see cref="MultiPortService.OnPortDisconnected"/>;
    /// the old design only released on the MCP close_port tool, so every other
    /// close path leaked ~120 KB per tag for the life of the process.</summary>
    public ConcurrentDictionary<string, DataBufferService> Buffers { get; } = new();

    /// <summary>Upper bound for <see cref="Buffers"/>. 64 tags × ~120 KB ≈ 7.7 MB
    /// worst case; a client needing more concurrent live ports is pathological.</summary>
    private const int MaxTrackedTags = 64;

    // Creation order of tracked tags, used only when the cap is exceeded.
    // Entries may be stale (already removed) and are validated/pruned on use.
    // Never touched on the per-packet path — only create and eviction.
    private readonly ConcurrentQueue<string> _tagOrder = new();

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

        // Uniform per-tag buffer release: MultiPortService raises this for
        // every close path (ClosePort, CloseAll) and for serial-level
        // disconnects, so no close path can leak a ~120 KB buffer.
        multiPort.OnPortDisconnected += tag => RemoveBuffer(tag);
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
    /// the empty tag, otherwise the per-tag buffer (created on demand). Fast
    /// path is a single hash lookup — the old GetOrAdd ran a factory-aware
    /// probe on every RX packet even when the tag already existed.</summary>
    public DataBufferService BufferFor(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return Buffer;
        if (Buffers.TryGetValue(tag, out var existing)) return existing;
        var created = new DataBufferService();
        if (Buffers.TryAdd(tag, created))
        {
            _tagOrder.Enqueue(tag);
            EnforceTagCap(tag);
            return created;
        }
        // Lost a creation race against another thread: the winner's instance
        // is the shared one (same semantics as the old GetOrAdd).
        return Buffers[tag];
    }

    /// <summary>Keeps <see cref="Buffers"/> within <see cref="MaxTrackedTags"/>.
    /// Prefers evicting a tag whose port is no longer open (its history is
    /// stale); falls back to the oldest created tag when every tracked tag
    /// still has a live port. The just-added tag is never the victim, and the
    /// creation-order queue is rebuilt (pruning stale entries) in the same
    /// pass. Only ever runs on the create path — never per packet.</summary>
    private void EnforceTagCap(string justAdded)
    {
        if (Buffers.Count <= MaxTrackedTags) return;

        string? victim = null;
        var survivors = new List<string>();
        while (_tagOrder.TryDequeue(out var candidate))
        {
            if (!Buffers.ContainsKey(candidate)) continue; // stale queue entry
            if (victim == null && candidate != justAdded && !IsPortOpen(candidate))
            {
                victim = candidate;
                continue;
            }
            survivors.Add(candidate);
        }

        if (victim == null)
        {
            // Every tracked tag still has a live port: evict the oldest.
            for (var i = 0; i < survivors.Count && victim == null; i++)
            {
                if (survivors[i] != justAdded) victim = survivors[i];
            }
            if (victim != null) survivors.Remove(victim);
        }

        if (victim != null) Buffers.TryRemove(victim, out _);
        foreach (var survivor in survivors) _tagOrder.Enqueue(survivor);
    }

    private bool IsPortOpen(string tag) => MultiPort.GetPort(tag)?.Service.IsOpen == true;

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

    /// <summary>Runs a tool body, converting any unhandled exception into the
    /// stable failure envelope (code INTERNAL) instead of letting raw exception
    /// text reach the client as an SDK-generated isError content — the
    /// {"success":false,"error":{code,message}} contract holds even when a
    /// dependency throws.</summary>
    public async Task<string> Guard(Func<Task<string>> body)
    {
        try
        {
            return await body().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ToolError(ErrorCodes.Internal, $"Unexpected server error ({ex.GetType().Name}): {ex.Message}");
        }
    }
}
