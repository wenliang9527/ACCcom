using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class SendHistoryBufferTests
{
    [Fact]
    public void Add_appends_entries_oldest_first()
    {
        var sut = new SendHistoryBuffer(50);

        sut.Add("one");
        sut.Add("two");
        sut.Add("three");

        Assert.Equal(new[] { "one", "two", "three" }, sut.Entries);
        Assert.Equal(3, sut.Count);
    }

    [Fact]
    public void Add_moves_existing_entry_to_end()
    {
        var sut = new SendHistoryBuffer(50);

        sut.Add("one");
        sut.Add("two");
        sut.Add("one");

        // Dedupe: "one" moves to the end instead of duplicating.
        Assert.Equal(new[] { "two", "one" }, sut.Entries);
    }

    [Fact]
    public void Add_ignores_empty_and_whitespace()
    {
        var sut = new SendHistoryBuffer(50);

        sut.Add("");
        sut.Add("   ");
        sut.Add(null);
        sut.Add("real");

        Assert.Single(sut.Entries);
        Assert.Equal("real", sut.Entries[0]);
    }

    [Fact]
    public void Add_evicts_oldest_beyond_capacity()
    {
        var sut = new SendHistoryBuffer(3);

        sut.Add("a");
        sut.Add("b");
        sut.Add("c");
        sut.Add("d");

        Assert.Equal(new[] { "b", "c", "d" }, sut.Entries);
    }

    [Fact]
    public void Add_dedupe_does_not_evict_below_capacity()
    {
        var sut = new SendHistoryBuffer(2);

        sut.Add("a");
        sut.Add("b");
        sut.Add("a"); // dedupe: moves to end, count stays 2

        Assert.Equal(new[] { "b", "a" }, sut.Entries);
    }

    [Fact]
    public void Capacity_clamps_to_at_least_one()
    {
        var sut = new SendHistoryBuffer(0);

        sut.Add("x");

        Assert.Single(sut.Entries);
    }

    [Fact]
    public void Clear_resets_entries()
    {
        var sut = new SendHistoryBuffer(50);
        sut.Add("a");
        sut.Add("b");

        sut.Clear();

        Assert.Empty(sut.Entries);
    }

    [Fact]
    public void TryNavigate_empty_history_returns_false()
    {
        var sut = new SendHistoryBuffer(50);

        var result = sut.TryNavigate(1, out var text, out var caret);

        Assert.False(result);
        Assert.Null(text);
        Assert.Equal(0, caret);
    }

    [Fact]
    public void TryNavigate_moves_backward_through_entries()
    {
        var sut = new SendHistoryBuffer(50);
        sut.Add("a");
        sut.Add("b");
        sut.Add("c");

        // First Up press lands on the newest entry.
        Assert.True(sut.TryNavigate(-1, out var text, out var caret));
        Assert.Equal("c", text);
        Assert.Equal(1, caret);

        Assert.True(sut.TryNavigate(-1, out text, out caret));
        Assert.Equal("b", text);

        Assert.True(sut.TryNavigate(-1, out text, out caret));
        Assert.Equal("a", text);
    }

    [Fact]
    public void TryNavigate_forward_returns_to_draft_slot()
    {
        var sut = new SendHistoryBuffer(50);
        sut.Add("a");
        sut.Add("b");

        Assert.True(sut.TryNavigate(-1, out _, out _)); // b
        Assert.True(sut.TryNavigate(-1, out _, out _)); // a
        Assert.True(sut.TryNavigate(1, out _, out _));  // b
        Assert.True(sut.TryNavigate(1, out var text, out var caret)); // draft

        Assert.Equal("", text);
        Assert.Equal(0, caret);
    }

    [Fact]
    public void TryNavigate_clamps_at_oldest_and_draft()
    {
        var sut = new SendHistoryBuffer(50);
        sut.Add("a");

        // Up past the only entry stays at it.
        Assert.True(sut.TryNavigate(-1, out var text, out _));
        Assert.Equal("a", text);
        Assert.True(sut.TryNavigate(-1, out text, out _));
        Assert.Equal("a", text);

        // Down past the newest returns to draft.
        Assert.True(sut.TryNavigate(1, out text, out _));
        Assert.Equal("", text);
        Assert.True(sut.TryNavigate(1, out text, out _));
        Assert.Equal("", text);
    }

    [Fact]
    public void Add_resets_navigation_to_draft()
    {
        var sut = new SendHistoryBuffer(50);
        sut.Add("a");
        sut.Add("b");

        Assert.True(sut.TryNavigate(-1, out var text, out _));
        Assert.Equal("b", text);

        // A new send resets the cursor so the next Up press sees the newest entry.
        sut.Add("c");
        Assert.True(sut.TryNavigate(-1, out text, out _));
        Assert.Equal("c", text);
    }

    [Fact]
    public void Entries_returns_a_copy()
    {
        var sut = new SendHistoryBuffer(50);
        sut.Add("a");

        var snapshot = sut.Entries as string[] ?? sut.Entries.ToArray();
        snapshot[0] = "mutated";

        Assert.Equal("a", sut.Entries[0]);
    }
}
