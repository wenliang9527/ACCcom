namespace ACCcom.McpServer.Tools;

/// <summary>Shared normalization for enum-valued free-text tool arguments
/// (direction filters, clear targets). Unknown values surface a structured
/// error instead of a silent no-match: an agent that typos "TX " or "receive"
/// would otherwise read an empty result as "nothing happened on the wire" —
/// the same failure mode the tag-guard comments already describe.</summary>
internal static class ToolArgs
{
    /// <summary>Normalizes a direction filter: trims, canonicalizes the known
    /// values to uppercase, maps null/empty/"all" to null (no filter). With
    /// <paramref name="allowSys"/> (traffic_log) SYS is accepted too. Returns
    /// the canonical direction or null; <paramref name="error"/> is set when
    /// the value matches none of the documented directions.</summary>
    public static string? NormalizeDirection(string? direction, bool allowSys, out string? error)
    {
        error = null;
        var v = direction?.Trim();
        if (string.IsNullOrEmpty(v) || v.Equals("all", StringComparison.OrdinalIgnoreCase))
            return null;
        if (v.Equals("rx", StringComparison.OrdinalIgnoreCase)) return "RX";
        if (v.Equals("tx", StringComparison.OrdinalIgnoreCase)) return "TX";
        if (allowSys && v.Equals("sys", StringComparison.OrdinalIgnoreCase)) return "SYS";
        error = $"Unknown direction '{direction}' (expected {(allowSys ? "RX, TX, or SYS" : "RX or TX")})";
        return null;
    }

    /// <summary>Normalizes clear_buffer's target: null/empty/"all" → "all";
    /// rx/tx canonicalized. Sets <paramref name="error"/> for anything else —
    /// DataBufferService.Clear keeps every entry for unknown values, which the
    /// tool would otherwise report as a successful clear.</summary>
    public static string NormalizeClearTarget(string? target, out string? error)
    {
        error = null;
        var v = target?.Trim();
        if (string.IsNullOrEmpty(v) || v.Equals("all", StringComparison.OrdinalIgnoreCase)) return "all";
        if (v.Equals("rx", StringComparison.OrdinalIgnoreCase)) return "RX";
        if (v.Equals("tx", StringComparison.OrdinalIgnoreCase)) return "TX";
        error = $"Unknown clear target '{target}' (expected rx, tx, or all)";
        return "all";
    }
}
