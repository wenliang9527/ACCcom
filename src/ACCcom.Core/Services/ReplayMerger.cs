using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

/// <summary>
/// Merges the RX and TX entry lists of a text-format session export into a
/// single chronologically-ordered stream. Both the Core JSONL replay path and
/// the UI text replay were hand-rolling this interleave — one shared merger
/// keeps the ordering contract identical everywhere.
/// </summary>
public static class ReplayMerger
{
    /// <summary>Concatenates both lists and sorts by timestamp (stable: equal
    /// timestamps preserve RX-then-TX order). The lists are not modified.</summary>
    public static List<LogEntry> MergeByTimestamp(IEnumerable<LogEntry> rxEntries, IEnumerable<LogEntry> txEntries)
    {
        var merged = new List<LogEntry>();
        if (rxEntries != null) merged.AddRange(rxEntries);
        if (txEntries != null) merged.AddRange(txEntries);
        // OrderBy is a stable sort; List.Sort is NOT, and the old implementation
        // leaned on it — with more than one equal-timestamp entry per side the
        // order was arbitrary. RX-then-TX for ties also matches the UI's
        // interleave path (its <= tie-break puts RX first), so all three paths
        // now agree. Export/replay only, so the extra buffer is free.
        return merged.OrderBy(static e => e.Timestamp).ToList();
    }
}
