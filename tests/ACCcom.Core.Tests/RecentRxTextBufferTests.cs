using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class RecentRxTextBufferTests
{
    [Fact]
    public void Add_appends_and_find_latest_returns_most_recent()
    {
        var sut = new RecentRxTextBuffer(cap: 512, trimChunk: 128);
        sut.Add("first");
        sut.Add("second contains OK here");

        Assert.Equal("second contains OK here", sut.FindLatestContaining("OK"));
    }

    [Fact]
    public void Find_latest_when_multiples_match()
    {
        var sut = new RecentRxTextBuffer(cap: 512, trimChunk: 128);
        sut.Add("OK v1");
        sut.Add("nope");
        sut.Add("OK v2");

        var result = sut.FindLatestContaining("OK");

        Assert.Equal("OK v2", result);
    }

    [Fact]
    public void Find_no_match_returns_null()
    {
        var sut = new RecentRxTextBuffer(cap: 512, trimChunk: 128);
        sut.Add("hello");

        Assert.Null(sut.FindLatestContaining("zzz"));
    }

    [Fact]
    public void Find_empty_or_null_pattern_returns_null()
    {
        var sut = new RecentRxTextBuffer(cap: 512, trimChunk: 128);
        sut.Add("hello");

        Assert.Null(sut.FindLatestContaining(""));
        Assert.Null(sut.FindLatestContaining(null!));
    }

    [Fact]
    public void Find_is_case_insensitive()
    {
        var sut = new RecentRxTextBuffer(cap: 512, trimChunk: 128);
        sut.Add("say HeLLo");

        Assert.Equal("say HeLLo", sut.FindLatestContaining("hello"));
    }

    [Fact]
    public void Add_ignores_empty_and_null()
    {
        var sut = new RecentRxTextBuffer(cap: 512, trimChunk: 128);
        sut.Add("");
        sut.Add("   ");
        sut.Add(null);
        sut.Add("real");

        Assert.Equal(1, sut.Count);
        Assert.Equal("real", sut.FindLatestContaining("real"));
    }

    [Fact]
    public void Clear_empties_buffer()
    {
        var sut = new RecentRxTextBuffer(cap: 512, trimChunk: 128);
        sut.Add("hello");

        sut.Clear();

        Assert.Equal(0, sut.Count);
        Assert.Null(sut.FindLatestContaining("hello"));
    }

    [Fact]
    public void Trims_only_when_backlog_exceeds_cap_plus_chunk()
    {
        // cap 10, chunk 5: adding up to 15 entries must not trim...
        var sut = new RecentRxTextBuffer(cap: 10, trimChunk: 5);
        for (int i = 0; i < 15; i++) sut.Add($"t{i}");
        Assert.Equal(15, sut.Count);

        // ...the 16th crosses cap+chunk and trims back to exactly cap.
        sut.Add("t15");
        Assert.Equal(10, sut.Count);

        // Oldest entries were dropped; newest survived.
        Assert.Null(sut.FindLatestContaining("t0"));
        Assert.NotNull(sut.FindLatestContaining("t15"));
    }

    [Fact]
    public void Trim_keeps_list_between_cap_and_cap_plus_chunk()
    {
        var sut = new RecentRxTextBuffer(cap: 10, trimChunk: 5);
        for (int i = 0; i < 100; i++)
        {
            sut.Add($"t{i}");
            Assert.InRange(sut.Count, 1, 15);
        }

        Assert.InRange(sut.Count, 10, 15); // after the last add, still within cap..cap+chunk
        Assert.NotNull(sut.FindLatestContaining("t99"));
    }

    [Fact]
    public void Cap_clamps_to_at_least_one()
    {
        var sut = new RecentRxTextBuffer(cap: 0, trimChunk: 1);
        sut.Add("a");
        sut.Add("b");
        sut.Add("c");

        Assert.InRange(sut.Count, 1, 2);
    }

    [Fact]
    public async Task Concurrent_add_and_find_never_throws()
    {
        var sut = new RecentRxTextBuffer(cap: 64, trimChunk: 16);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var writer = Task.Run(() =>
        {
            int i = 0;
            while (!cts.IsCancellationRequested)
            {
                sut.Add($"msg {i++ % 100} with OK");
                Thread.SpinWait(10);
            }
        });

        var reader = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                _ = sut.FindLatestContaining("OK");
                Thread.SpinWait(10);
            }
        });

        await Task.WhenAll(writer, reader);

        // Both completed without throwing; Add + trims stayed consistent.
        Assert.InRange(sut.Count, 1, 80);
    }
}