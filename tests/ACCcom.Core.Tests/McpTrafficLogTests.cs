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
}