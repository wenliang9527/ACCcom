using System.Text.Json;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

/// <summary>Parses one line of the shared MCP traffic JSONL log into a display
/// entry. Lives in Core so the GUI tail window and the tests share the exact
/// same field handling (missing fields default to empty, the ISO timestamp is
/// sliced to HH:mm:ss.fff).</summary>
public static class TrafficLogParser
{
    /// <summary>Parses a log line. Returns false (and a null-safe empty entry)
    /// for blank or malformed lines — the log is a live file another process
    /// writes, so a torn or non-object line must never crash the tail view.</summary>
    public static bool TryParseLine(string? line, out TrafficLogEntry entry)
    {
        entry = new TrafficLogEntry();
        if (string.IsNullOrWhiteSpace(line)) return false;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            entry.Time = SliceTime(GetString(root, "timestamp"));
            entry.Direction = GetString(root, "direction");
            entry.Tool = GetString(root, "tool");
            entry.Tag = GetString(root, "portTag");
            entry.Hex = GetString(root, "rawHex");
            entry.Text = GetString(root, "text");
            return true;
        }
        catch { /* malformed JSON or a non-object root — skip the line */ }
        return false;
    }

    private static string GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var prop) ? prop.GetString() ?? "" : "";

    /// <summary>ISO timestamps are kept full in the log; the traffic view shows
    /// only the wall-clock part ("2026-09-11T08:30:12.3456789+08:00" → "08:30:12.345").</summary>
    private static string SliceTime(string timestamp)
        => timestamp.Length >= 23 ? timestamp[11..23] : timestamp;
}
