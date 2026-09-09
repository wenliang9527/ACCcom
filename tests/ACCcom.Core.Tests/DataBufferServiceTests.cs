using System;
using System.Threading.Tasks;
using Xunit;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class DataBufferServiceTests
{
    private static LogEntry MakeEntry(int id, string direction = "RX", string text = "hello", string hex = "48 45 4C 4C 4F")
    {
        return new LogEntry
        {
            Id = id,
            Timestamp = DateTime.Now,
            Direction = direction,
            PortTag = "COM1",
            RawHex = hex,
            Text = text
        };
    }

    [Fact]
    public void AddEntry_then_Count_returns_1()
    {
        // Arrange
        var sut = new DataBufferService();
        var entry = MakeEntry(1);

        // Act
        sut.AddEntry(entry);

        // Assert
        Assert.Equal(1, sut.Count());
    }

    [Fact]
    public void Constructor_zero_capacity_clamps_to_one()
    {
        // capacity 0 would divide by zero on ring wrap (_head % _capacity);
        // the constructor clamps to a minimum of 1 so the buffer stays usable.
        var sut = new DataBufferService(0);
        sut.AddEntry(MakeEntry(1));
        sut.AddEntry(MakeEntry(2));

        Assert.Equal(1, sut.Count());
        Assert.Equal(2, sut.GetEntriesSince(0)[0].Id); // most recent survives
    }

    [Fact]
    public void Constructor_negative_capacity_clamps_to_one()
    {
        var sut = new DataBufferService(-5);
        sut.AddEntry(MakeEntry(1));

        Assert.Equal(1, sut.Count());
    }

    [Fact]
    public void AddEntry_then_GetEntriesSince_returns_only_newer()
    {
        // Arrange
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1));
        sut.AddEntry(MakeEntry(2));
        sut.AddEntry(MakeEntry(3));

        // Act
        var result = sut.GetEntriesSince(2);

        // Assert
        Assert.Single(result);
        Assert.Equal(3, result[0].Id);
    }

    [Fact]
    public void GetEntriesSince_after_ring_wrap_returns_only_newer_tail()
    {
        // Arrange: capacity 4 — adding 6 entries evicts the two oldest, so the
        // ring's logical start is no longer index 0 (the binary-search tail
        // lookup must handle the wrap).
        var sut = new DataBufferService(capacity: 4);
        for (int i = 1; i <= 6; i++)
            sut.AddEntry(MakeEntry(i));

        // Act
        var result = sut.GetEntriesSince(3);

        // Assert
        Assert.Equal(new[] { 4, 5, 6 }, result.Select(e => e.Id));
    }

    [Fact]
    public void GetEntriesSince_filters_direction_and_limit()
    {
        // Arrange
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, direction: "RX"));
        sut.AddEntry(MakeEntry(2, direction: "TX"));
        sut.AddEntry(MakeEntry(3, direction: "RX"));
        sut.AddEntry(MakeEntry(4, direction: "TX"));

        // Act
        var rx = sut.GetEntriesSince(0, direction: "RX");
        var limited = sut.GetEntriesSince(0, direction: "TX", limit: 1);

        // Assert
        Assert.Equal(new[] { 1, 3 }, rx.Select(e => e.Id));
        Assert.Single(limited);
        Assert.Equal(2, limited[0].Id);
    }

    [Fact]
    public void GetEntriesSince_scannedMaxId_AdvancesCorrectly()
    {
        // Cursor semantics: with a direction filter + limit, the cursor advances
        // to the last RETURNED entry (entries beyond the limit are still unread);
        // when nothing is returned (the filter skipped the tail), it advances to
        // the highest scanned Id so the next poll does not rescann skipped entries.
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, direction: "RX"));
        sut.AddEntry(MakeEntry(2, direction: "TX"));
        sut.AddEntry(MakeEntry(3, direction: "RX"));
        sut.AddEntry(MakeEntry(4, direction: "TX"));

        // TX filter + limit 1 returns [2]; beyond-limit TX (4) is still unread,
        // so the cursor is 2 (last returned), not 4.
        var limited = sut.GetEntriesSince(0, direction: "TX", limit: 1, out var limitedScan);
        Assert.Equal(2, limited[0].Id);
        Assert.Equal(2, limitedScan);

        // Next poll from 2 picks up the remaining TX (4) — nothing lost.
        var rest = sut.GetEntriesSince(limitedScan, direction: "TX", limit: 10, out var restScan);
        Assert.Equal(4, rest[0].Id);
        Assert.Equal(4, restScan);

        // No filter, all consumed: cursor advances to the scan max.
        var all = sut.GetEntriesSince(0, null, 10, out var allScan);
        Assert.Equal(4, allScan);

        // RX filter over the whole tail (not truncated): returns [1,3]; cursor
        // advances to the scanned max (4) so the next poll skips the TX entries
        // it already saw and does not rescann them.
        var rx = sut.GetEntriesSince(0, "RX", 10, out var rxScan);
        Assert.Equal(new[] { 1, 3 }, rx.Select(e => e.Id));
        Assert.Equal(4, rxScan);
        Assert.Empty(sut.GetEntriesSince(rxScan, "RX", 10));
    }

    [Fact]
    public void GetEntriesSince_scannedMaxId_DefaultsToId_WhenEmptyOrAllConsumed()
    {
        var sut = new DataBufferService();

        // Empty buffer: scannedMaxId stays at the requested id.
        var empty = sut.GetEntriesSince(7, null, 0, out var emptyScan);
        Assert.Empty(empty);
        Assert.Equal(7, emptyScan);

        // All consumed: stays at id.
        sut.AddEntry(MakeEntry(1));
        var done = sut.GetEntriesSince(1, null, 0, out var doneScan);
        Assert.Empty(done);
        Assert.Equal(1, doneScan);
    }

    [Fact]
    public void Clear_removes_all_entries()
    {
        // Arrange
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1));
        sut.AddEntry(MakeEntry(2));

        // Act
        sut.Clear();

        // Assert
        Assert.Equal(0, sut.Count());
    }

    [Fact]
    public void Clear_with_direction_removes_only_matching()
    {
        // Arrange
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, direction: "RX"));
        sut.AddEntry(MakeEntry(2, direction: "TX"));

        // Act
        sut.Clear("rx");

        // Assert
        Assert.Equal(1, sut.Count());
    }

    [Fact]
    public void CountWhere_filters_correctly()
    {
        // Arrange
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, direction: "RX"));
        sut.AddEntry(MakeEntry(2, direction: "TX"));
        sut.AddEntry(MakeEntry(3, direction: "RX"));

        // Act
        var count = sut.CountWhere(e => e.Direction == "TX");

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public void CountWhere_null_predicate_throws()
    {
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, direction: "RX"));

        Assert.Throws<ArgumentNullException>(() => sut.CountWhere(null!));
    }

    [Fact]
    public void CountDirection_tracks_incrementally()
    {
        // Arrange
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, direction: "RX"));
        sut.AddEntry(MakeEntry(2, direction: "TX"));
        sut.AddEntry(MakeEntry(3, direction: "RX"));

        // Act & Assert — O(1) counts match what a full scan would report.
        Assert.Equal(2, sut.CountDirection("RX"));
        Assert.Equal(1, sut.CountDirection("TX"));
        Assert.Equal(3, sut.Count());
    }

    [Fact]
    public void CountDirection_survives_ring_overwrite()
    {
        // Arrange — capacity 3, write 6 entries so the first three are evicted.
        var sut = new DataBufferService(capacity: 3);
        for (int i = 1; i <= 6; i++)
            sut.AddEntry(MakeEntry(i, direction: i % 2 == 0 ? "TX" : "RX"));

        // Act & Assert — only entries 4..6 remain: TX(4), RX(5), TX(6).
        Assert.Equal(3, sut.Count());
        Assert.Equal(1, sut.CountDirection("RX"));
        Assert.Equal(2, sut.CountDirection("TX"));
    }

    [Fact]
    public void CountDirection_resets_on_directional_clear()
    {
        // Arrange
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, direction: "RX"));
        sut.AddEntry(MakeEntry(2, direction: "TX"));
        sut.AddEntry(MakeEntry(3, direction: "RX"));

        // Act
        sut.Clear("rx");

        // Assert
        Assert.Equal(1, sut.Count());
        Assert.Equal(0, sut.CountDirection("RX"));
        Assert.Equal(1, sut.CountDirection("TX"));
    }

    [Fact]
    public async Task WaitForMatchAsync_returns_matching_entry()
    {
        // Arrange — WaitForMatchAsync registers the waiter before it awaits,
        // so calling it first and then adding the entry is deterministic: the
        // add always happens after registration and is delivered via the
        // waiter path. The old version raced a fixed 50ms Task.Delay against
        // the 500ms wait and flaked under parallel load.
        var sut = new DataBufferService();

        // Act
        var waitTask = sut.WaitForMatchAsync("hello", timeoutMs: 2000);
        sut.AddEntry(MakeEntry(1, text: "hello world"));
        var result = await waitTask;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result!.Id);
    }

    [Fact]
    public async Task WaitForMatchAsync_returns_null_on_timeout()
    {
        // Arrange
        var sut = new DataBufferService();

        // Act
        var result = await sut.WaitForMatchAsync("no_match", timeoutMs: 100);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task WaitForMatchAsync_with_direction_filter()
    {
        // Arrange — deterministic ordering: waiter registered first, entry
        // added after (delivered via the waiter path).
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, direction: "TX", text: "hello"));

        // Act
        var waitTask = sut.WaitForMatchAsync("hello", direction: "RX", timeoutMs: 2000);
        sut.AddEntry(MakeEntry(2, direction: "RX", text: "hello"));
        var result = await waitTask;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result!.Id);
    }

    [Fact]
    public async Task WaitForMatchAsync_exact_mode()
    {
        // Arrange
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, text: "hello world"));

        // Act - "hello" does not exact-match "hello world"
        var result = await sut.WaitForMatchAsync("hello", matchMode: "exact", timeoutMs: 100);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task WaitForMatchAsync_regex_mode()
    {
        // Arrange — deterministic ordering, same as the other waiter tests.
        var sut = new DataBufferService();

        // Act
        var waitTask = sut.WaitForMatchAsync("^AB.*CD$", matchMode: "regex", matchHex: true, timeoutMs: 2000);
        sut.AddEntry(MakeEntry(1, hex: "AB 12 34 CD", text: "ignored"));
        var result = await waitTask;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result!.Id);
    }

    [Fact]
    public async Task WaitForMatchAsync_checks_existing_buffer()
    {
        // Arrange
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, text: "already here"));

        // Act - no delay needed, entry already in buffer
        var result = await sut.WaitForMatchAsync("already here", timeoutMs: 200);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result!.Id);
    }

    [Fact]
    public void AddEntry_null_is_noop()
    {
        var sut = new DataBufferService();

        sut.AddEntry(null);

        Assert.Equal(0, sut.Count());
        Assert.Empty(sut.GetEntriesSince(0));
    }

    [Fact]
    public async Task AddEntry_null_does_not_break_waiters()
    {
        var sut = new DataBufferService();
        var wait = sut.WaitForMatchAsync("target", timeoutMs: 2000);

        // A null entry must not NRE in the waiter scan or match anything.
        sut.AddEntry(null);
        sut.AddEntry(MakeEntry(1, text: "target"));

        var result = await wait;
        Assert.NotNull(result);
        Assert.Equal(1, result!.Id);
    }

    [Fact]
    public async Task WaitForMatchAsync_zero_timeout_returns_null_without_throwing()
    {
        var sut = new DataBufferService();

        // A non-positive timeout used to throw inside Task.Delay; it must
        // instead behave as "no wait" and return null promptly.
        var result = await sut.WaitForMatchAsync("never", timeoutMs: 0);

        Assert.Null(result);
    }

    [Fact]
    public async Task WaitForMatchAsync_negative_timeout_returns_null_without_throwing()
    {
        var sut = new DataBufferService();

        var result = await sut.WaitForMatchAsync("never", timeoutMs: -100);

        Assert.Null(result);
    }

    [Fact]
    public async Task WaitForMatchAsync_zero_timeout_still_finds_existing_entry()
    {
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, text: "already here"));

        // The existing-buffer scan happens before the timeout path, so a
        // matching entry already present is still returned even with a
        // zero timeout.
        var result = await sut.WaitForMatchAsync("already here", timeoutMs: 0);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Id);
    }
}
