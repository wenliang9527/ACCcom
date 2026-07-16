using System.Text.Json;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class FileExportService
{
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };
    private const int MaxRetries = 1;
    private const int RetryDelayMs = 500;

    /// <summary>
    /// Exports entries to a plain text file at the given path.
    /// </summary>
    public void ExportToText(IEnumerable<LogEntry> entries, string filePath)
    {
        var text = string.Join(Environment.NewLine,
            entries.Select(e =>
            {
                var ts = e.Timestamp.ToString("HH:mm:ss.fff");
                return $"[{ts}][{e.Direction}] {e.RawHex} | {e.Text}";
            }));

        WriteWithRetry(filePath, text);
    }

    /// <summary>
    /// Exports entries to a JSON file at the given path.
    /// </summary>
    public void ExportToJson(IEnumerable<LogEntry> entries, string filePath)
    {
        var data = entries.Select(e => new
        {
            timestamp = e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            direction = e.Direction,
            hex = e.RawHex,
            text = e.Text,
            fields = e.Fields?.Select(f => new
            {
                name = f.Name,
                offset = f.Offset,
                length = f.Length,
                rawHex = f.RawHex,
                value = f.DisplayValue,
                severity = f.Severity.ToString()
            })
        });
        var json = JsonSerializer.Serialize(data, IndentedOptions);
        WriteWithRetry(filePath, json);
    }

    /// <summary>
    /// Exports entries to a CSV file at the given path.
    /// Columns: Timestamp, Direction, RawHex, Text, ParsedFields
    /// </summary>
    public static void ExportToCsv(IEnumerable<LogEntry> entries, string filePath)
    {
        var lines = new List<string> { "Timestamp,Direction,RawHex,Text,ParsedFields" };
        foreach (var e in entries)
        {
            var ts = e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var fields = e.Fields is { Count: > 0 }
                ? string.Join(";", e.Fields.Select(f => $"{f.Name}={f.DisplayValue}"))
                : "";
            lines.Add($"\"{ts}\",\"{e.Direction}\",\"{Escape(e.RawHex)}\",\"{Escape(e.Text)}\",\"{Escape(fields)}\"");
        }
        WriteWithRetry(filePath, string.Join(Environment.NewLine, lines));
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Replace("\"", "\"\"");
    }

    private static void WriteWithRetry(string filePath, string content)
    {
        Exception? lastException = null;

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0)
                Thread.Sleep(RetryDelayMs);

            try
            {
                using var sw = new StreamWriter(filePath);
                sw.Write(content);
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        throw new IOException($"[FileExportService] Write failed after {MaxRetries + 1} attempts: {lastException?.Message}", lastException);
    }

    /// <summary>
    /// Replays entries from a log file. Returns parsed RX/TX entries and counts.
    /// </summary>
    public (List<LogEntry> rxEntries, List<LogEntry> txEntries, int parsed, int skipped) ReplayFromFile(string filePath, int startId)
    {
        var rxEntries = new List<LogEntry>();
        var txEntries = new List<LogEntry>();
        var lines = File.ReadAllLines(filePath);
        int parsed = 0, skipped = 0;
        int nextId = startId;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) { skipped++; continue; }

            var match = System.Text.RegularExpressions.Regex.Match(line,
                @"^\[(\d{2}:\d{2}:\d{2}\.\d{3})\]\[(RX|TX)\]\s+([0-9A-Fa-f\s]+)\|");

            if (!match.Success) { skipped++; continue; }

            if (!DateTime.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var time))
            {
                skipped++;
                continue;
            }

            var direction = match.Groups[2].Value;
            var hex = match.Groups[3].Value.Trim();

            var entry = new LogEntry
            {
                Id = nextId++,
                Timestamp = time,
                Direction = direction,
                RawHex = hex,
                Text = line.Contains('|') ? line[(line.IndexOf('|') + 1)..].Trim() : ""
            };

            if (direction == "RX") rxEntries.Add(entry);
            else txEntries.Add(entry);
            parsed++;
        }

        return (rxEntries, txEntries, parsed, skipped);
    }
}
