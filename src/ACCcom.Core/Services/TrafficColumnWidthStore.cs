namespace ACCcom.Core.Services;

/// <summary>
/// Read/write helpers for the MCP traffic window's persisted column widths,
/// keyed by column name (GridViewColumn has no Tag property, and the previous
/// index-keyed scheme silently remapped every width whenever a column was
/// inserted or reordered). Missing entries fall back to the column's XAML
/// default, and widths that would make a column unusable are ignored rather
/// than persisted.
/// </summary>
public static class TrafficColumnWidthStore
{
    /// <summary>Applies persisted widths to the given columns, falling back to
    /// <paramref name="defaults"/> (parallel to <paramref name="names"/>) when
    /// a key is missing or the value is not a usable width.</summary>
    public static double[] ResolveWidths(
        IReadOnlyDictionary<string, double>? persisted,
        IReadOnlyList<string> names,
        IReadOnlyList<double> defaults)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(defaults);
        if (names.Count != defaults.Count)
            throw new ArgumentException("Names and defaults must have the same length.");
        var resolved = new double[defaults.Count];
        for (int i = 0; i < defaults.Count; i++)
        {
            resolved[i] = defaults[i];
            if (persisted != null && persisted.TryGetValue(names[i], out var w) && IsUsableWidth(w))
                resolved[i] = w;
        }
        return resolved;
    }

    /// <summary>Collects widths into a persistable dictionary keyed by column
    /// name (parallel to the widths list). Columns whose width is not usable
    /// (zero/negative/NaN) are skipped so a collapsed or mid-layout column
    /// never poisons the saved state.</summary>
    public static Dictionary<string, double> CollectWidths(
        IReadOnlyList<string> names, IReadOnlyList<double> widths)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(widths);
        if (names.Count != widths.Count)
            throw new ArgumentException("Names and widths must have the same length.");
        var result = new Dictionary<string, double>();
        for (int i = 0; i < widths.Count; i++)
        {
            if (IsUsableWidth(widths[i]))
                result[names[i]] = widths[i];
        }
        return result;
    }

    private static bool IsUsableWidth(double w) => w > 0 && !double.IsNaN(w) && !double.IsInfinity(w);
}
