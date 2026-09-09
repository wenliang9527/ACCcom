using System.Text.Json;
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

    /// <summary>Per-tag data buffers, created on demand and isolated from each other.</summary>
    public Dictionary<string, DataBufferService> Buffers { get; } = new();

    public McpTrafficLog TrafficLog { get; } = McpTrafficLog.Shared;

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public ToolContext(MultiPortService multiPort, ISerialService defaultSerial)
    {
        MultiPort = multiPort;
        Serial = defaultSerial;

        Serial.OnDataReceived += entry =>
        {
            Buffer.AddEntry(entry);
            TrafficLog.Record(entry.Id, "rx", entry.Direction, entry.RawHex ?? "", entry.Text ?? "", "");
        };

        multiPort.OnDataReceived += entry =>
        {
            var tag = entry.PortTag ?? "";
            var buffer = BufferFor(tag);
            buffer.AddEntry(entry);
            TrafficLog.Record(entry.Id, "rx", entry.Direction, entry.RawHex ?? "", entry.Text ?? "", tag);
        };
    }

    /// <summary>Returns the buffer for a tag: the default single-port buffer for
    /// the empty tag, otherwise the per-tag buffer (created on demand).</summary>
    public DataBufferService BufferFor(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return Buffer;
        lock (Buffers)
        {
            if (!Buffers.TryGetValue(tag, out var buffer))
            {
                buffer = new DataBufferService();
                Buffers[tag] = buffer;
            }
            return buffer;
        }
    }

    /// <summary>Drops a tag's per-tag buffer when its port is closed, so a later
    /// reopen of the same tag starts with a clean buffer and does not surface
    /// stale entries from the previous session.</summary>
    public void RemoveBuffer(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return;
        lock (Buffers)
        {
            Buffers.Remove(tag);
        }
    }

    public string RawJson(object obj) =>
        JsonSerializer.Serialize(obj, JsonOpts);
}