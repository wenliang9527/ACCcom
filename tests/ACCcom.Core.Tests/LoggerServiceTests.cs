using System.IO;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class LoggerServiceTests : IDisposable
{
    private readonly string _logDir;

    public LoggerServiceTests()
    {
        _logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ACCcom", "logs");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_logDir))
                Directory.Delete(_logDir, true);
        }
        catch { }
    }

    [Fact]
    public void Constructor_CreatesLogDirectory()
    {
        using var logger = new LoggerService();
        Assert.True(Directory.Exists(_logDir));
    }

    [Fact]
    public void Constructor_SetsCurrentLogPath()
    {
        using var logger = new LoggerService();
        Assert.NotNull(logger.CurrentLogPath);
        Assert.NotEmpty(logger.CurrentLogPath);
        Assert.EndsWith(".log", logger.CurrentLogPath);
    }

    [Fact]
    public void Write_CreatesLogFile()
    {
        using var logger = new LoggerService();
        var entry = new LogEntry
        {
            Id = 1,
            Timestamp = DateTime.Now,
            Direction = "RX",
            RawHex = "AA 55 03",
            Text = "Test data"
        };
        logger.Write(entry);
        Assert.True(File.Exists(logger.CurrentLogPath));
    }

    [Fact]
    public void Write_WritesFormattedLine()
    {
        var path = "";
        using (var logger = new LoggerService())
        {
            path = logger.CurrentLogPath;
            logger.Write(new LogEntry
            {
                Id = 1,
                Timestamp = new DateTime(2024, 1, 15, 10, 30, 0, 0),
                Direction = "TX",
                RawHex = "BB CC",
                Text = "output"
            });
        }
        var content = File.ReadAllText(path);
        Assert.Contains("10:30:00", content);
        Assert.Contains("[TX]", content);
        Assert.Contains("BB CC", content);
        Assert.Contains("output", content);
    }

    [Fact]
    public void Write_MultipleEntries_AppendsToFile()
    {
        var path = "";
        using (var logger = new LoggerService())
        {
            path = logger.CurrentLogPath;
            logger.Write(new LogEntry { Direction = "RX", Timestamp = DateTime.Now, RawHex = "01", Text = "a" });
            logger.Write(new LogEntry { Direction = "RX", Timestamp = DateTime.Now, RawHex = "02", Text = "b" });
        }
        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void CurrentLogPath_AfterConstructor_NotEmpty()
    {
        using var logger = new LoggerService();
        Assert.False(string.IsNullOrEmpty(logger.CurrentLogPath));
    }

    [Fact]
    public void Dispose_ClosesFileHandle()
    {
        var logger = new LoggerService();
        var path = logger.CurrentLogPath;
        logger.Write(new LogEntry { Direction = "RX", Timestamp = DateTime.Now, RawHex = "FF", Text = "" });
        logger.Dispose();
        var content = File.ReadAllText(path);
        Assert.NotEmpty(content);
    }
}
