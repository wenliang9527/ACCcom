using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class PcapExportServiceTests : IDisposable
{
    private readonly string _tempDir;

    public PcapExportServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "pcap_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void ExportToPcap_CreatesFile()
    {
        var service = new PcapExportService();
        var path = Path.Combine(_tempDir, "test.pcap");
        var entries = new List<LogEntry>();

        service.ExportToPcap(entries, path);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void ExportToPcap_WritesGlobalHeader()
    {
        var service = new PcapExportService();
        var path = Path.Combine(_tempDir, "test.pcap");
        var entries = new List<LogEntry>();

        service.ExportToPcap(entries, path);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(24, bytes.Length);
        Assert.Equal(0xD4, bytes[0]);
        Assert.Equal(0xC3, bytes[1]);
        Assert.Equal(0xB2, bytes[2]);
        Assert.Equal(0xA1, bytes[3]);
        Assert.Equal(2, bytes[4]);
        Assert.Equal(4, bytes[6]);
    }

    [Fact]
    public void ExportToPcap_SingleEntry_WritesPacketRecord()
    {
        var service = new PcapExportService();
        var path = Path.Combine(_tempDir, "test.pcap");
        var entries = new List<LogEntry>
        {
            new()
            {
                Id = 1,
                Timestamp = new DateTime(2025, 1, 15, 10, 30, 45, 123, DateTimeKind.Utc),
                Direction = "TX",
                RawHex = "AA BB CC"
            }
        };

        service.ExportToPcap(entries, path);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(24 + 16 + 4, bytes.Length); // header + packet header + 4 bytes data (3 hex + 1 direction)
    }

    [Fact]
    public void ExportToPcap_TXEntry_Prepends0x01()
    {
        var service = new PcapExportService();
        var path = Path.Combine(_tempDir, "test.pcap");
        var entries = new List<LogEntry>
        {
            new() { Id = 1, Timestamp = DateTime.UtcNow, Direction = "TX", RawHex = "AA" }
        };

        service.ExportToPcap(entries, path);

        var bytes = File.ReadAllBytes(path);
        var packetStart = 24 + 16;
        Assert.Equal(0x01, bytes[packetStart]);
        Assert.Equal(0xAA, bytes[packetStart + 1]);
    }

    [Fact]
    public void ExportToPcap_RXEntry_Prepends0x02()
    {
        var service = new PcapExportService();
        var path = Path.Combine(_tempDir, "test.pcap");
        var entries = new List<LogEntry>
        {
            new() { Id = 1, Timestamp = DateTime.UtcNow, Direction = "RX", RawHex = "BB" }
        };

        service.ExportToPcap(entries, path);

        var bytes = File.ReadAllBytes(path);
        var packetStart = 24 + 16;
        Assert.Equal(0x02, bytes[packetStart]);
        Assert.Equal(0xBB, bytes[packetStart + 1]);
    }

    [Fact]
    public void ExportToPcap_MultipleEntries_WritesAllRecords()
    {
        var service = new PcapExportService();
        var path = Path.Combine(_tempDir, "test.pcap");
        var entries = new List<LogEntry>
        {
            new() { Id = 1, Timestamp = DateTime.UtcNow, Direction = "TX", RawHex = "AA" },
            new() { Id = 2, Timestamp = DateTime.UtcNow, Direction = "RX", RawHex = "BB CC" },
            new() { Id = 3, Timestamp = DateTime.UtcNow, Direction = "TX", RawHex = "" }
        };

        service.ExportToPcap(entries, path);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(24 + (16 + 2) + (16 + 3) + (16 + 1), bytes.Length);
    }

    [Fact]
    public void ExportToPcap_EmptyRawHex_WritesOnlyDirectionByte()
    {
        var service = new PcapExportService();
        var path = Path.Combine(_tempDir, "test.pcap");
        var entries = new List<LogEntry>
        {
            new() { Id = 1, Timestamp = DateTime.UtcNow, Direction = "TX", RawHex = "" }
        };

        service.ExportToPcap(entries, path);

        var bytes = File.ReadAllBytes(path);
        var packetStart = 24 + 16;
        Assert.Equal(0x01, bytes[packetStart]);
        Assert.Equal(1, bytes.Length - packetStart);
    }

    [Theory]
    [InlineData("AA BB CC", new byte[] { 0xAA, 0xBB, 0xCC })]
    [InlineData("AA-BB-CC", new byte[] { 0xAA, 0xBB, 0xCC })]
    [InlineData("AABBCC", new byte[] { 0xAA, 0xBB, 0xCC })]
    [InlineData("", new byte[0])]
    [InlineData("  ", new byte[0])]
    public void HexToBytes_ParsesVariousFormats(string input, byte[] expected)
    {
        var result = PcapExportService.HexToBytes(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void HexToBytes_SingleByte()
    {
        var result = PcapExportService.HexToBytes("0A");
        Assert.Equal(new byte[] { 0x0A }, result);
    }
}
