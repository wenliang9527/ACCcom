using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ACCcom.Core.Models;

namespace ACCcom.McpServer.Tools;

/// <summary>
/// Lean response DTOs plus source-generated serialization for the per-call hot
/// paths (read_data cursor polls). Two token-saving policies are baked in:
///
/// 1. Sparse output — every optional column is null-omitted when empty or
///    default (portTag:"", truncated:false, empty text/hex, envelope tag:""),
///    so a plain single-port ASCII poll carries no dead weight.
/// 2. Selectable columns — <see cref="LeanFields"/> masks which columns a
///    read_data caller wants (fields parameter), e.g. fields=text drops the
///    hex column (the largest one on binary streams) entirely.
///
/// timestamps are formatted once at projection time to ISO-8601 with
/// millisecond precision (yyyy-MM-ddTHH:mm:ss.fff + kind offset) instead of
/// the default 7-digit round-trip form — a fixed format shared by every tool
/// response that carries a timestamp.
/// </summary>
internal static class McpJson
{
    public static string Serialize(ReadDataResponse response) =>
        JsonSerializer.Serialize(response, McpJsonContext.Default.ReadDataResponse);

    /// <summary>ISO-8601 ms precision: 2026-09-26T10:30:00.123+08:00 (local),
    /// …Z (UTC), no suffix (Unspecified — test fixtures).</summary>
    public static string FormatTimestamp(DateTime timestamp) =>
        timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffK", CultureInfo.InvariantCulture);

    /// <summary>Parses the read_data fields selector ("text,hex" or null for
    /// all columns). Unknown tokens are reported instead of silently ignored so
    /// a typo does not masquerade as "field missing from response".</summary>
    public static bool TryParseFields(string? fields, out LeanFields mask, out string? error)
    {
        mask = LeanFields.All;
        error = null;
        if (string.IsNullOrWhiteSpace(fields)) return true;

        var parsed = LeanFields.None;
        foreach (var raw in fields.Split(','))
        {
            var token = raw.Trim();
            if (token.Length == 0) continue;
            switch (token.ToLowerInvariant())
            {
                case "id": parsed |= LeanFields.Id; break;
                case "timestamp": parsed |= LeanFields.Timestamp; break;
                case "direction": parsed |= LeanFields.Direction; break;
                case "porttag": parsed |= LeanFields.PortTag; break;
                case "text": parsed |= LeanFields.Text; break;
                case "hex": parsed |= LeanFields.Hex; break;
                case "truncated": parsed |= LeanFields.Truncated; break;
                default:
                    error = $"Unknown field '{token}' (valid: id,timestamp,direction,portTag,text,hex,truncated)";
                    return false;
            }
        }
        mask = parsed == LeanFields.None ? LeanFields.All : parsed;
        return true;
    }

    /// <summary>
    /// UI-free projection of a buffer entry: drops HighlightColor /
    /// IsSearchMatch / Fields (irrelevant to MCP clients and pure token bloat),
    /// omits empty/default columns (sparse JSON), applies the opt-in maxLength
    /// truncation to the selected text/hex columns, and honors the fields mask.
    /// Used by read_data and by the wait-style tools' embedded entry responses.
    /// </summary>
    public static LeanEntry Lean(LogEntry entry, int maxLength = 0, LeanFields fields = LeanFields.All)
    {
        var lean = new LeanEntry();
        if (fields.HasFlag(LeanFields.Id)) lean.id = entry.Id;
        if (fields.HasFlag(LeanFields.Timestamp)) lean.timestamp = FormatTimestamp(entry.Timestamp);

        if (fields.HasFlag(LeanFields.Direction) && !string.IsNullOrEmpty(entry.Direction))
            lean.direction = entry.Direction;
        if (fields.HasFlag(LeanFields.PortTag) && !string.IsNullOrEmpty(entry.PortTag))
            lean.portTag = entry.PortTag;

        var truncated = false;
        if (fields.HasFlag(LeanFields.Text) && !string.IsNullOrEmpty(entry.Text))
        {
            var text = entry.Text;
            if (maxLength > 0 && text.Length > maxLength)
            {
                text = text[..maxLength] + "\u2026";
                truncated = true;
            }
            lean.text = text;
        }
        if (fields.HasFlag(LeanFields.Hex) && !string.IsNullOrEmpty(entry.RawHex))
        {
            var hex = entry.RawHex;
            if (maxLength > 0 && hex.Length > maxLength)
            {
                hex = hex[..maxLength] + "\u2026";
                truncated = true;
            }
            lean.hex = hex;
        }
        if (fields.HasFlag(LeanFields.Truncated) && truncated) lean.truncated = true;
        return lean;
    }
}

/// <summary>Column mask for read_data's fields parameter. All = full response
/// (the default — existing callers keep byte-comparable shape minus the sparse
/// omission policy).</summary>
[Flags]
internal enum LeanFields
{
    None = 0,
    Id = 1 << 0,
    Timestamp = 1 << 1,
    Direction = 1 << 2,
    PortTag = 1 << 3,
    Text = 1 << 4,
    Hex = 1 << 5,
    Truncated = 1 << 6,
    All = Id | Timestamp | Direction | PortTag | Text | Hex | Truncated,
}

internal sealed class LeanEntry
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? id { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? timestamp { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? direction { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? portTag { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? text { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? hex { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? truncated { get; set; }
}

internal sealed class ReadDataResponse
{
    public bool success { get; set; }
    public ReadDataBody data { get; set; } = null!;
}

internal sealed class ReadDataBody
{
    public List<LeanEntry> entries { get; set; } = new();
    public int count { get; set; }
    public int latestId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? tag { get; set; }
}

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(ReadDataResponse))]
internal partial class McpJsonContext : JsonSerializerContext;
