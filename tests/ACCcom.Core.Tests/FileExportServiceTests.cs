using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class FileExportServiceTests : IDisposable
{
    private readonly FileExportService _sut = new();
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { }
        }
    }

    private string NewTempPath(string ext = ".txt")
    {
        var path = Path.Combine(Path.GetTempPath(), $"acctest_{Guid.NewGuid():N}{ext}");
        _tempFiles.Add(path);
        return path;
    }

    private static LogEntry MakeEntry(int id, string direction = "RX", string text = "hello",
        string hex = "48 45 4C 4C 4F")
    {
        return new LogEntry
        {
            Id = id,
            Timestamp = new DateTime(2025, 6, 12, 14, 30, 45, 123),
            Direction = direction,
            PortTag = "COM1",
            RawHex = hex,
            Text = text
        };
    }

    [Fact]
    public void ExportToText_writes_correct_format()
    {
        // Arrange
        var entries = new List<LogEntry> { MakeEntry(1) };
        var path = NewTempPath(".txt");

        // Act
        _sut.ExportToText(entries, path);
        var content = File.ReadAllText(path);

        // Assert
        Assert.Contains("[14:30:45.123]", content);
        Assert.Contains("[RX]", content);
        Assert.Contains("48 45 4C 4C 4F", content);
        Assert.Contains("hello", content);
    }

    [Fact]
    public void ExportToJson_writes_valid_json()
    {
        // Arrange
        var entries = new List<LogEntry> { MakeEntry(1), MakeEntry(2, direction: "TX") };
        var path = NewTempPath(".json");

        // Act
        _sut.ExportToJson(entries, path);
        var content = File.ReadAllText(path);

        // Assert
        var doc = JsonDocument.Parse(content);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public void ExportToCsv_writes_header_and_rows()
    {
        // Arrange
        var entries = new List<LogEntry> { MakeEntry(1), MakeEntry(2) };
        var path = NewTempPath(".csv");

        // Act
        FileExportService.ExportToCsv(entries, path);
        var lines = File.ReadAllLines(path);

        // Assert
        Assert.True(lines.Length >= 3); // header + 2 data rows
        Assert.Contains("Timestamp", lines[0]);
        Assert.Contains("Direction", lines[0]);
    }

    [Fact]
    public void ExportToCsv_escapes_quotes_in_text()
    {
        // Arrange
        var entry = MakeEntry(1, text: "say \"hello\"");
        var entries = new List<LogEntry> { entry };
        var path = NewTempPath(".csv");

        // Act
        FileExportService.ExportToCsv(entries, path);
        var content = File.ReadAllText(path);

        // Assert - quotes should be escaped (doubled in CSV)
        Assert.Contains("\"\"hello\"\"", content);
    }

    [Fact]
    public void ReplayFromFile_parses_valid_entries()
    {
        // Arrange
        var exportPath = NewTempPath(".txt");
        var entries = new List<LogEntry>
        {
            MakeEntry(1, direction: "RX", text: "alpha"),
            MakeEntry(2, direction: "TX", text: "beta")
        };
        _sut.ExportToText(entries, exportPath);

        // Act
        var (rxEntries, txEntries, parsed, skipped) = _sut.ReplayFromFile(exportPath, startId: 100);

        // Assert
        Assert.Equal(2, parsed);
        Assert.Equal(0, skipped);
        Assert.Single(rxEntries);
        Assert.Single(txEntries);
        Assert.Equal(100, rxEntries[0].Id);
        Assert.Equal(101, txEntries[0].Id);
    }

    [Fact]
    public void ReplayFromFile_skips_invalid_lines()
    {
        // Arrange
        var path = NewTempPath(".txt");
        File.WriteAllText(path, "this is not valid\n[14:30:45.123][RX] 48 45 | hello\nanother bad line\n");

        // Act
        var (rxEntries, txEntries, parsed, skipped) = _sut.ReplayFromFile(path, startId: 0);

        // Assert
        Assert.Equal(1, parsed);
        Assert.True(skipped >= 1);
    }

    [Fact]
    public void ReplayFromFile_returns_correct_counts()
    {
        // Arrange
        var exportPath = NewTempPath(".txt");
        var entries = new List<LogEntry>
        {
            MakeEntry(1, direction: "RX"),
            MakeEntry(2, direction: "RX"),
            MakeEntry(3, direction: "TX"),
            MakeEntry(4, direction: "RX"),
            MakeEntry(5, direction: "TX")
        };
        _sut.ExportToText(entries, exportPath);

        // Act
        var (rxEntries, txEntries, parsed, skipped) = _sut.ReplayFromFile(exportPath, startId: 0);

        // Assert
        Assert.Equal(5, parsed);
        Assert.Equal(3, rxEntries.Count);
        Assert.Equal(2, txEntries.Count);
    }
}
