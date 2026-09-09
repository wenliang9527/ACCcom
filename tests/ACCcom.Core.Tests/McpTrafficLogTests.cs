using System.Text.Json;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class McpTrafficLogTests
{
    private static string TempPath()
        => Path.Combine(Path.GetTempPath(), $"mcp-traffic-{Guid.NewGuid():N}.jsonl");

    [Fact]
    public void Record_WritesJsonlLine_WithToolAndDirection()
    {
        var path = TempPath();
        using (var log = new McpTrafficLog(path))
        {
            log.Record(1, "send", "TX", "48 45 4C 4C 4F", "HELLO");
        }

        var line = File.ReadAllLines(path).Single();
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        Assert.Equal("send", root.GetProperty("tool").GetString());
        Assert.Equal("TX", root.GetProperty("direction").GetString());
        Assert.Equal("48 45 4C 4C 4F", root.GetProperty("rawHex").GetString());
        Assert.Equal("HELLO", root.GetProperty("text").GetString());
        Assert.True(root.TryGetProperty("timestamp", out _));
        Assert.Equal("", root.GetProperty("portTag").GetString());
    }

    [Fact]
    public void Record_WithTag_WritesPortTagField()
    {
        var path = TempPath();
        using (var log = new McpTrafficLog(path))
        {
            log.Record(1, "send", "TX", "AA", "a", "sensor_a");
        }

        var line = File.ReadAllLines(path).Single();
        using var doc = JsonDocument.Parse(line);
        Assert.Equal("sensor_a", doc.RootElement.GetProperty("portTag").GetString());
    }

    [Fact]
    public void Record_AppendsMultipleLines_InOrder()
    {
        var path = TempPath();
        using (var log = new McpTrafficLog(path))
        {
            log.Record(1, "send", "TX", "AA", "a");
            log.Record(2, "send", "TX", "BB", "b");
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"rawHex\":\"AA\"", lines[0]);
        Assert.Contains("\"rawHex\":\"BB\"", lines[1]);
    }

    [Fact]
    public void Record_NullText_StillWrites()
    {
        var path = TempPath();
        using (var log = new McpTrafficLog(path))
        {
            log.Record(1, "read", "RX", "01 02", "");
        }

        var lines = File.ReadAllLines(path);
        var line = Assert.Single(lines);
        Assert.Contains("\"text\":\"\"", line);
    }

    [Fact]
    public void Rotation_Respects_MaxLines_And_Uses_InstancePath()
    {
        var path = TempPath();
        var oldMax = McpTrafficLog.MaxLines;
        try
        {
            McpTrafficLog.MaxLines = 3;
            using (var log = new McpTrafficLog(path))
            {
                log.Record(1, "send", "TX", "AA", "a");
                log.Record(2, "send", "TX", "BB", "b");
                log.Record(3, "send", "TX", "CC", "c"); // hits MaxLines → rotate
            }

            // After rotation the main file is fresh (0-1 lines), the old content
            // moved to the .1 backup.
            var mainLines = File.ReadAllLines(path);
            var backupLines = File.ReadAllLines(path + ".1");
            Assert.True(mainLines.Length <= 1, $"main should be fresh, got {mainLines.Length}");
            Assert.Equal(3, backupLines.Length);
        }
        finally
        {
            McpTrafficLog.MaxLines = oldMax;
        }
    }
}