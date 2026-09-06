using System.Text;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

/// <summary>Formats entries for clipboard copy as
/// <c>[time][direction][HEX/TXT] value</c> lines. Extracted from
/// DataFlowViewModel.GetFormattedCopyText so the exact line format is locked
/// by tests; the writer is injectable to avoid depending on console IO.</summary>
public static class EntryTextFormatter
{
    /// <summary>Writes each entry's non-empty hex and text representations as
    /// separate lines (an entry carrying both produces two lines).</summary>
    public static string Format(IEnumerable<LogEntry> entries, string direction)
    {
        var sb = new StringBuilder();
        Format(entries, direction, sb);
        return sb.ToString();
    }

    public static void Format(IEnumerable<LogEntry> entries, string direction, StringBuilder target)
    {
        foreach (var entry in entries)
        {
            var hex = entry.RawHex ?? "";
            var text = entry.Text ?? "";
            var time = entry.Timestamp.ToString("HH:mm:ss.fff");
            if (!string.IsNullOrEmpty(hex))
                target.AppendLine($"[{time}][{direction}][HEX] {hex}");
            if (!string.IsNullOrEmpty(text))
                target.AppendLine($"[{time}][{direction}][TXT] {text}");
        }
    }
}
