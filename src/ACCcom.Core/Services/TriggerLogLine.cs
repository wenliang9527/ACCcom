using System;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

/// <summary>
/// Formats a <see cref="LogEntry"/> into the single-line record that a
/// trigger's SaveToFile action appends. Extracted from TriggerViewModel so
/// the on-disk trigger-log line format is unit-testable without the UI layer.
/// </summary>
public static class TriggerLogLine
{
    /// <summary>E.g. "[12:34:56.789] RX hello".</summary>
    public static string Format(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return $"[{entry.Timestamp:HH:mm:ss.fff}] {entry.Direction} {entry.Text}";
    }
}