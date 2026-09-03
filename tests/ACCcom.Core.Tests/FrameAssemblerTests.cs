using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class FrameAssemblerTests
{
    private static LogEntry MakeEntry(string hex, string text = "")
    {
        return new LogEntry
        {
            Id = 0,
            Timestamp = DateTime.Now,
            Direction = "RX",
            PortTag = "main",
            RawHex = hex,
            Text = text
        };
    }

    private static FrameAssemblerConfig DefaultConfig()
    {
        return new FrameAssemblerConfig
        {
            Header = "AA 55",
            LengthFieldOffset = 2,
            LengthFieldSize = 1,
            MaxFrameSize = 4096,
            PartialFrameTimeoutMs = 2000,
            Enabled = true
        };
    }

    [Fact]
    public void Single_fragment_matches_header_and_length_completes_immediately()
    {
        var config = DefaultConfig();
        var assembler = new FrameAssembler(config);

        LogEntry? assembled = null;
        assembler.OnFrameAssembled += e => assembled = e;

        var entry = MakeEntry("AA 55 04 01 02 03");
        assembler.Feed(entry);

        Assert.NotNull(assembled);
    }

    [Fact]
    public void Two_fragments_are_assembled_correctly()
    {
        var config = DefaultConfig();
        var assembler = new FrameAssembler(config);

        LogEntry? assembled = null;
        assembler.OnFrameAssembled += e => assembled = e;

        var frag1 = MakeEntry("AA 55 06");
        var frag2 = MakeEntry("01 02 03 04");

        assembler.Feed(frag1);
        Assert.Null(assembled);

        assembler.Feed(frag2);
        Assert.NotNull(assembled);
        Assert.Equal("AA 55 06 01 02 03 04", assembled!.RawHex);
    }

    [Fact]
    public void Three_fragments_are_assembled_correctly()
    {
        var config = DefaultConfig();
        var assembler = new FrameAssembler(config);

        LogEntry? assembled = null;
        assembler.OnFrameAssembled += e => assembled = e;

        var frag1 = MakeEntry("AA 55");
        var frag2 = MakeEntry("08");
        var frag3 = MakeEntry("01 02 03 04 05");

        assembler.Feed(frag1);
        Assert.Null(assembled);

        assembler.Feed(frag2);
        Assert.Null(assembled);

        assembler.Feed(frag3);
        Assert.NotNull(assembled);
        Assert.Equal("AA 55 08 01 02 03 04 05", assembled!.RawHex);
    }

    [Fact]
    public void Fragment_without_header_is_discarded()
    {
        var config = DefaultConfig();
        var assembler = new FrameAssembler(config);

        LogEntry? assembled = null;
        assembler.OnFrameAssembled += e => assembled = e;

        var badEntry = MakeEntry("BB CC 04 01 02 03");
        assembler.Feed(badEntry);

        Assert.Null(assembled);

        var goodEntry = MakeEntry("AA 55 04 01 02 03");
        assembler.Feed(goodEntry);

        Assert.NotNull(assembled);
    }

    [Fact]
    public void Partial_frame_times_out_and_is_discarded()
    {
        var config = DefaultConfig();
        config.PartialFrameTimeoutMs = 100;
        var assembler = new FrameAssembler(config);

        LogEntry? assembled = null;
        assembler.OnFrameAssembled += e => assembled = e;

        assembler.Feed(MakeEntry("AA 55 08 01 02"));
        Assert.Null(assembled);

        Thread.Sleep(350);

        Assert.Null(assembled);
    }

    [Fact]
    public void No_length_field_completes_on_header_match()
    {
        var config = DefaultConfig();
        config.LengthFieldOffset = -1;
        var assembler = new FrameAssembler(config);

        LogEntry? assembled = null;
        assembler.OnFrameAssembled += e => assembled = e;

        assembler.Feed(MakeEntry("AA 55 01 02 03"));
        Assert.NotNull(assembled);
    }

    [Fact]
    public void Empty_header_matches_any_frame()
    {
        var config = DefaultConfig();
        config.Header = "";
        config.LengthFieldOffset = -1;
        var assembler = new FrameAssembler(config);

        LogEntry? assembled = null;
        assembler.OnFrameAssembled += e => assembled = e;

        assembler.Feed(MakeEntry("01 02 03 04"));
        Assert.NotNull(assembled);
    }

    [Fact]
    public void Disabled_assembler_does_not_process()
    {
        var config = DefaultConfig();
        config.Enabled = false;
        var assembler = new FrameAssembler(config);

        LogEntry? assembled = null;
        assembler.OnFrameAssembled += e => assembled = e;

        assembler.Feed(MakeEntry("AA 55 04 01 02 03"));
        Assert.Null(assembled);
    }

    [Fact]
    public void Dispose_stops_timer_and_clears_state()
    {
        var config = DefaultConfig();
        var assembler = new FrameAssembler(config);

        assembler.Dispose();

        LogEntry? assembled = null;
        assembler.OnFrameAssembled += e => assembled = e;

        assembler.Feed(MakeEntry("AA 55 04 01 02 03"));
        Assert.Null(assembled);
    }

    [Fact]
    public void Reset_clears_partial_frame()
    {
        var config = DefaultConfig();
        var assembler = new FrameAssembler(config);

        LogEntry? assembled = null;
        assembler.OnFrameAssembled += e => assembled = e;

        assembler.Feed(MakeEntry("AA 55"));
        assembler.Reset();

        assembler.Feed(MakeEntry("AA 55 04 01 02 03"));
        Assert.NotNull(assembled);
    }

    [Fact]
    public void Many_fragments_beyond_initial_buffer_assemble_correctly()
    {
        // The assembler accumulates hex in a growable char[] (initial 256);
        // feed enough fragments to span several growth steps. Length field
        // counts the whole frame (2 header + 1 length + payload), so choose a
        // payload that fills it exactly and aligns to the 7-byte feed chunks.
        var config = DefaultConfig();
        var assembler = new FrameAssembler(config);

        LogEntry? assembled = null;
        assembler.OnFrameAssembled += e => assembled = e;

        int payloadBytes = 147; // frame = 2 + 1 + 147 = 150 bytes; 147 = 7 * 21 chunks
        assembler.Feed(MakeEntry("AA 55"));
        assembler.Feed(MakeEntry(payloadBytes.ToString("X2"))); // length field 0x96 = 150
        for (int i = 0; i < payloadBytes; i += 7)
        {
            var frag = string.Join(" ",
                Enumerable.Range(i, Math.Min(7, payloadBytes - i))
                    .Select(b => b.ToString("X2")));
            assembler.Feed(MakeEntry(frag));
        }

        Assert.NotNull(assembled);
        var expected = "AA 55 " + payloadBytes.ToString("X2") + " " +
            string.Join(" ", Enumerable.Range(0, payloadBytes).Select(b => b.ToString("X2")));
        Assert.Equal(expected, assembled!.RawHex);
    }

    [Fact]
    public void Fragment_after_complete_starts_new_frame()
    {
        // A completed frame resets the accumulation buffer; the next frame must
        // not inherit stale bytes from the previous one.
        var config = DefaultConfig();
        var assembler = new FrameAssembler(config);
        var frames = new List<LogEntry>();
        assembler.OnFrameAssembled += e => frames.Add(e);

        assembler.Feed(MakeEntry("AA 55 02 01")); // frame 1 completes immediately
        assembler.Feed(MakeEntry("AA 55 02 02")); // frame 2: must start fresh

        Assert.Equal(2, frames.Count);
        Assert.Equal("AA 55 02 01", frames[0].RawHex);
        Assert.Equal("AA 55 02 02", frames[1].RawHex);
    }
}
