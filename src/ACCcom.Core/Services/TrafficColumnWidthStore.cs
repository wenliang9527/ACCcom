namespace ACCcom.Core.Services;

/// <summary>
/// Read/write helpers for the MCP traffic window's persisted column widths,
/// keyed by zero-based column index (GridViewColumn has no Tag property, so the
/// index is the stable key — same scheme as FieldGridColumnWidths). Missing or
/// out-of-range entries fall back to the column's XAML default, and widths that
/// would make a column unusable are ignored rather than persisted.
/// </summary>
public static class TrafficColumnWidthStore
{
    /// <summary>Applies persisted widths to the given columns, falling back to
    /// <paramref name="defaults"/> (same length as the XAML column list) when a
    /// key is missing or the value is not a usable width.</summary>
    public static double[] ResolveWidths(
        IReadOnlyDictionary<int, double>? persisted,
        IReadOnlyList<double> defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        var resolved = new double[defaults.Count];
        for (int i = 0; i < defaults.Count; i++)
        {
            resolved[i] = defaults[i];
            if (persisted != null && persisted.TryGetValue(i, out var w) && IsUsableWidth(w))
                resolved[i] = w;
        }
        return resolved;
    }

    /// <summary>Collects widths into a persistable dictionary. Columns whose
    /// width is not usable (zero/negative/NaN) are skipped so a collapsed or
    /// mid-layout column never poisons the saved state.</summary>
    public static Dictionary<int, double> CollectWidths(IReadOnlyList<double> widths)
    {
        ArgumentNullException.ThrowIfNull(widths);
        var result = new Dictionary<int, double>();
        for (int i = 0; i < widths.Count; i++)
        {
            if (IsUsableWidth(widths[i]))
                result[i] = widths[i];
        }
        return result;
    }

    private static bool IsUsableWidth(double w) => w > 0 && !double.IsNaN(w) && !double.IsInfinity(w);
}
