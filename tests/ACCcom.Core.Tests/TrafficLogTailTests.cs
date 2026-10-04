using System.Text;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class TrafficLogTailTests
{
    private const string Bom = "EF BB BF";

    [Fact]
    public void Split_CompleteLines_AdvancesPastAll()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"a\":1}\n{\"b\":2}\n");

        var (lines, offset) = TrafficLogTail.Split(bytes, bytes.Length, 0);

        Assert.Equal(2, lines.Count);
        Assert.Equal("{\"a\":1}", lines[0]);
        Assert.Equal("{\"b\":2}", lines[1]);
        Assert.Equal(bytes.Length, offset);
    }

    [Fact]
    public void Split_TrailingPartialLine_HeldBack()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"a\":1}\n{\"b\":2");

        var (lines, offset) = TrafficLogTail.Split(bytes, bytes.Length, 0);

        var line = Assert.Single(lines);
        Assert.Equal("{\"a\":1}", line);
        Assert.Equal(8, offset); // just past the first '\n', partial tail excluded
    }

    [Fact]
    public void Split_NoCompleteLine_KeepsOffset()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"a\":1");

        var (lines, offset) = TrafficLogTail.Split(bytes, bytes.Length, 314);

        Assert.Empty(lines);
        Assert.Equal(314, offset);
    }

    [Fact]
    public void Split_ZeroCount_KeepsOffset()
    {
        var (lines, offset) = TrafficLogTail.Split([1, 2, 3], 0, 42);

        Assert.Empty(lines);
        Assert.Equal(42, offset);
    }

    [Fact]
    public void Split_BomAtFileStart_Stripped()
    {
        var body = Encoding.UTF8.GetBytes("{\"a\":1}\n");
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray();

        var (lines, offset) = TrafficLogTail.Split(bytes, bytes.Length, 0);

        var line = Assert.Single(lines);
        Assert.Equal("{\"a\":1}", line);
        Assert.Equal(bytes.Length, offset);
    }

    [Fact]
    public void Split_BomBytesAtNonzeroOffset_KeptAsContent()
    {
        // A mid-file read must never treat a leading EF BB BF as a BOM.
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("x\n")).ToArray();

        var (lines, _) = TrafficLogTail.Split(bytes, bytes.Length, 100);

        var line = Assert.Single(lines);
        Assert.Equal("\uFEFFx", line);
    }

    [Fact]
    public void Split_Crlf_CarriageReturnTrimmed()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"a\":1}\r\n{\"b\":2}\r\n");

        var (lines, offset) = TrafficLogTail.Split(bytes, bytes.Length, 0);

        Assert.Equal(2, lines.Count);
        Assert.Equal("{\"a\":1}", lines[0]);
        Assert.Equal("{\"b\":2}", lines[1]);
        Assert.Equal(bytes.Length, offset);
    }

    [Fact]
    public void Split_InteriorBlankLines_Preserved()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"a\":1}\n\n{\"b\":2}\n");

        var (lines, _) = TrafficLogTail.Split(bytes, bytes.Length, 0);

        Assert.Equal(3, lines.Count);
        Assert.Equal("", lines[1]);
    }

    [Fact]
    public void Split_CjkMultibyte_OffsetLandsOnByteBoundary()
    {
        // "你好" is 6 UTF-8 bytes; the offset arithmetic must stay in the byte
        // domain or the next Seek lands mid-character and corrupts the decode.
        var bytes = Encoding.UTF8.GetBytes("{\"text\":\"你好\"}\n{\"text\":\"OK\"}\n");

        var (lines, offset) = TrafficLogTail.Split(bytes, bytes.Length, 0);

        Assert.Equal(2, lines.Count);
        Assert.Equal(bytes.Length, offset);
        Assert.Contains("你好", lines[0]);
    }

    [Fact]
    public void Split_TornRecord_CompletesWholeOnNextRead()
    {
        // Flush 1: record A complete, record B torn mid-JSON by the writer's
        // buffer boundary. The tail must hold B back, then read it whole once
        // the writer completes it — never the headless remainder.
        var recordA = Encoding.UTF8.GetBytes("{\"id\":1}\n");
        var recordB = Encoding.UTF8.GetBytes("{\"id\":2,\"text\":\"KEY DOWN\"}\n");
        var recordC = Encoding.UTF8.GetBytes("{\"id\":3}\n");

        var torn = recordB.AsSpan(0, 12).ToArray(); // "{\"id\":2,\"tex"
        var chunk1 = recordA.Concat(torn).ToArray();
        var (lines1, offset1) = TrafficLogTail.Split(chunk1, chunk1.Length, 0);
        var lineA = Assert.Single(lines1);
        Assert.Equal("{\"id\":1}", lineA);
        Assert.Equal(recordA.Length, offset1);

        // Flush 2 completes B and adds C; the next read starts at offset1.
        var chunk2 = recordB.Concat(recordC).ToArray();
        var (lines2, offset2) = TrafficLogTail.Split(chunk2, chunk2.Length, offset1);

        Assert.Equal(2, lines2.Count);
        Assert.Equal(Encoding.UTF8.GetString(recordB[..^1]), lines2[0]);
        Assert.Equal("{\"id\":3}", lines2[1]);
        Assert.Equal(offset1 + chunk2.Length, offset2);
    }

    [Fact]
    public void Split_BomThenTornFirstLine_HeldBackWithoutBomLoss()
    {
        // A fresh log's first record torn after the BOM: nothing readable yet,
        // offset must stay at 0 so the BOM is still there on the next read.
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("{\"id\":1")).ToArray();

        var (lines, offset) = TrafficLogTail.Split(bytes, bytes.Length, 0);

        Assert.Empty(lines);
        Assert.Equal(0, offset);
    }

    [Fact]
    public void Split_HoldbackChainAcrossTwoGrows_ConsumesExactlyOnce()
    {
        // Three consecutive reads while the writer appends; the buffer of each
        // read holds the file's bytes from the current offset onward (the
        // caller seeks before reading), and each held-back prefix is consumed
        // exactly once when the writer completes it.
        const string bom = "\uFEFF";
        var first = Encoding.UTF8.GetBytes(bom + "{\"n\":1}\n");

        var (lines1, offset1) = TrafficLogTail.Split(first, first.Length, 0);
        Assert.Equal("{\"n\":1}", Assert.Single(lines1));

        var second = Encoding.UTF8.GetBytes("{\"n\":2,\"text\":\"你好\"}\n");
        var (lines2, offset2) = TrafficLogTail.Split(second, second.Length, offset1);
        Assert.Equal("{\"n\":2,\"text\":\"你好\"}", Assert.Single(lines2));
        Assert.Equal(offset1 + second.Length, offset2);

        var third = Encoding.UTF8.GetBytes("{\"n\":3}\n");
        var (lines3, offset3) = TrafficLogTail.Split(third, third.Length, offset2);
        Assert.Equal("{\"n\":3}", Assert.Single(lines3));
        Assert.Equal(offset2 + third.Length, offset3);
    }

    [Fact]
    public void Split_RealLogShape_ParsesViaTrafficLogParser()
    {
        // The exact wire shape McpTrafficLog writes (camelCase + BOM + CRLF),
        // split and fed through the same parser the window uses.
        const string iso = "2026-10-04T10:36:44.9123456+08:00";
        var line = $"{{\"id\":0,\"tool\":\"open_port\",\"timestamp\":\"{iso}\",\"direction\":\"SYS\",\"rawHex\":\"\",\"text\":\"COM15\",\"portTag\":\"\"}}";
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(line + "\r\n")).ToArray();

        var (lines, _) = TrafficLogTail.Split(bytes, bytes.Length, 0);

        Assert.True(TrafficLogParser.TryParseLine(lines[0], out var entry));
        Assert.Equal("SYS", entry.Direction);
        Assert.Equal("COM15", entry.Text);
        Assert.Equal("10:36:44.912", entry.Time);
    }
}
