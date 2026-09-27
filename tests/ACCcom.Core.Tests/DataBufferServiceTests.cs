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
    public void GetEntriesSince_direction_filter_is_case_insensitive()
    {
        // The direction filter matches OrdinalIgnoreCase, so "rx"/"R x"
        // variants must behave identically to "RX".
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, direction: "RX"));
        sut.AddEntry(MakeEntry(2, direction: "TX"));
        sut.AddEntry(MakeEntry(3, direction: "RX"));

        var lower = sut.GetEntriesSince(0, direction: "rx");
        var upper = sut.GetEntriesSince(0, direction: "RX");

        Assert.Equal(new[] { 1, 3 }, lower.Select(e => e.Id));
        Assert.Equal(new[] { 1, 3 }, upper.Select(e => e.Id));
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
    public void GetEntriesSince_usesArrivalCursor_notEntryId()
    {
        // Regression: entry ids come from independent RX/TX counters, so
        // arrival order interleaves non-monotonically (5,1,...). A cursor built
        // from Entry.Id skipped every TX entry whose counter lagged the RX the
        // consumer had already seen — after any RX traffic, later TX rows never
        // reached HTTP/MCP consumers.
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(5, direction: "RX"));

        var first = sut.GetEntriesSince(0, null, 0, out var cursor);
        Assert.Single(first);

        // TX arrives with a LOWER id than the RX the consumer already saw.
        sut.AddEntry(MakeEntry(1, direction: "TX"));
        var second = sut.GetEntriesSince(cursor, null, 0, out _);

        Assert.Single(second);
        Assert.Equal(1, second[0].Id);
    }

    [Fact]
    public void GetEntriesSince_afterDirectionClear_doesNotRedispatchSurvivors()
    {
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, direction: "RX"));
        sut.AddEntry(MakeEntry(2, direction: "TX"));
        sut.GetEntriesSince(0, null, 0, out var cursor);

        sut.Clear("rx");
        sut.AddEntry(MakeEntry(3, direction: "RX"));

        // Survivors keep their original sequence (a fresh one would push them
        // past the cursor and re-deliver them as duplicates); new arrivals
        // remain visible to the same cursor.
        var next = sut.GetEntriesSince(cursor, null, 0, out _);
        Assert.Single(next);
        Assert.Equal(3, next[0].Id);
    }

    [Fact]
    public void GetEntriesSince_afterClearAll_cursorStaysMonotonic()
    {
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1));
        sut.AddEntry(MakeEntry(2));
        sut.GetEntriesSince(0, null, 0, out var cursor);

        sut.Clear();
        sut.AddEntry(MakeEntry(3));

        // The sequence survives Clear: restarting at 1 would hide post-clear
        // entries from a pre-clear cursor until the sequence caught up.
        var next = sut.GetEntriesSince(cursor, null, 0, out _);
        Assert.Single(next);
        Assert.Equal(3, next[0].Id);
    }

    [Theory]
    [InlineData(false, 0, null)]
    [InlineData(true, 1, null)]
    [InlineData(true, 0, "TX")]
    public void GetEntriesSince_empty_result_is_owned_by_caller(bool hasEntry, int cursor, string? direction)
    {
        using var sut = new DataBufferService();
        using var other = new DataBufferService();
        if (hasEntry) sut.AddEntry(MakeEntry(1, direction: "RX"));

        var result = sut.GetEntriesSince(cursor, direction, 0, out var scanned);
        Assert.Empty(result);
        Assert.Equal(hasEntry ? 1 : cursor, scanned);
        try
        {
            result.Add(MakeEntry(99));

            Assert.Empty(sut.GetEntriesSince(cursor, direction));
            Assert.Empty(other.GetEntriesSince(0));
            Assert.Equal(hasEntry ? 1 : 0, sut.Count());
            Assert.Equal(0, other.Count());
        }
        finally
        {
            result.Clear();
        }
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
    public async Task WaitForMatchAsync_null_pattern_throws_argument_null()
    {
        // A null pattern would throw deep inside MatchesPattern's contains
        // branch (target.Contains(null)) after the waiter is registered —
        // reject it at the entry point.
        var sut = new DataBufferService();
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.WaitForMatchAsync(null!));
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

    [Fact]
    public async Task WaitForMatchAsync_immediate_match_unregisters_waiter()
    {
        // The old implementation registered the waiter inside the lock, then
        // returned early on an immediate match without ever removing it —
        // left-listed forever (cleared only by a later AddEntry sweep).
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, text: "already here"));

        var result = await sut.WaitForMatchAsync("already here", timeoutMs: 200);

        Assert.NotNull(result);
        Assert.Equal(0, sut.WaiterCount);
    }

    [Fact]
    public async Task WaitForMatchAsync_delivered_match_unregisters_waiter()
    {
        var sut = new DataBufferService();

        var waitTask = sut.WaitForMatchAsync("hello", timeoutMs: 2000);
        Assert.Equal(1, sut.WaiterCount); // registered, still pending

        sut.AddEntry(MakeEntry(1, text: "hello"));
        var result = await waitTask;

        Assert.NotNull(result);
        Assert.Equal(0, sut.WaiterCount);
    }

    [Fact]
    public async Task WaitForMatchAsync_timeout_unregisters_waiter()
    {
        // A timed-out waiter used to linger in _waiters until the next
        // AddEntry (potentially seconds/never under an idle port), so every
        // timed-out poll leaked a registration and O(n) sweeps grew with polls.
        var sut = new DataBufferService();

        var result = await sut.WaitForMatchAsync("never", timeoutMs: 50);

        Assert.Null(result);
        Assert.Equal(0, sut.WaiterCount);
    }

    [Fact]
    public async Task WaitForMatchAsync_many_timeouts_leave_no_waiter_leak()
    {
        var sut = new DataBufferService();

        // Idle-port polling pattern: repeated timed-out waits must not
        // accumulate registrations (the pre-fix leak accumulated until the
        // next incoming entry happened to sweep them out).
        var waits = new Task<LogEntry?>[50];
        for (int i = 0; i < waits.Length; i++)
            waits[i] = sut.WaitForMatchAsync($"never_{i}", timeoutMs: 200);

        var results = await Task.WhenAll(waits);

        Assert.All(results, Assert.Null);
        Assert.Equal(0, sut.WaiterCount);
    }

    [Fact]
    public async Task WaitForMatchAsync_many_matches_leave_no_waiter_leak()
    {
        var sut = new DataBufferService();

        var waits = new Task<LogEntry?>[50];
        for (int i = 0; i < waits.Length; i++)
            waits[i] = sut.WaitForMatchAsync($"ping{i}", timeoutMs: 5000);

        for (int i = 0; i < waits.Length; i++)
            sut.AddEntry(MakeEntry(i + 1, text: $"ping{i}"));

        var results = await Task.WhenAll(waits);

        Assert.All(results, Assert.NotNull);
        Assert.Equal(0, sut.WaiterCount);
    }

    // ── GetTailEntries (cursor-less "newest N" read path) ──

    [Fact]
    public void GetTailEntries_returns_newest_in_arrival_order()
    {
        var sut = new DataBufferService();
        for (int i = 1; i <= 5; i++)
            sut.AddEntry(MakeEntry(i, text: $"m{i}"));

        var tail = sut.GetTailEntries(3, null, out var scannedMaxId);

        Assert.Equal(new[] { "m3", "m4", "m5" }, tail.Select(e => e.Text));
        Assert.Equal(5, scannedMaxId); // arrival seq of the newest entry (seq starts at 1)
    }

    [Fact]
    public void GetTailEntries_tail_larger_than_count_returns_all()
    {
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, text: "only"));

        var tail = sut.GetTailEntries(100, null, out var scannedMaxId);

        Assert.Single(tail);
        Assert.Equal(1, scannedMaxId);
    }

    [Fact]
    public void GetTailEntries_empty_buffer_returns_empty_with_zero_cursor()
    {
        var sut = new DataBufferService();

        var tail = sut.GetTailEntries(10, null, out var scannedMaxId);

        Assert.Empty(tail);
        Assert.Equal(0, scannedMaxId);
    }

    [Fact]
    public void GetTailEntries_nonpositive_tail_returns_empty_but_reports_cursor()
    {
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, text: "x"));

        var tail = sut.GetTailEntries(0, null, out var scannedMaxId);

        Assert.Empty(tail);
        Assert.Equal(1, scannedMaxId); // cursor still points at the newest entry
    }

    [Fact]
    public void GetTailEntries_direction_filter_counts_matches_not_recent_rows()
    {
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, direction: "TX", text: "t1"));
        sut.AddEntry(MakeEntry(2, direction: "RX", text: "r1"));
        sut.AddEntry(MakeEntry(3, direction: "TX", text: "t2"));
        sut.AddEntry(MakeEntry(4, direction: "RX", text: "r2"));

        var tail = sut.GetTailEntries(1, "TX", out _);

        Assert.Equal("t2", Assert.Single(tail).Text);
    }

    [Fact]
    public void GetTailEntries_cursor_continues_into_incremental_poll_without_gap_or_duplicate()
    {
        var sut = new DataBufferService();
        for (int i = 1; i <= 4; i++)
            sut.AddEntry(MakeEntry(i, text: $"m{i}"));

        var tail = sut.GetTailEntries(2, null, out var cursor);

        // A tail read of the newest 2 hands the caller a cursor: continuing
        // with GetEntriesSince(cursor) must return nothing already seen and
        // nothing missing.
        Assert.Equal(new[] { "m3", "m4" }, tail.Select(e => e.Text));
        Assert.Empty(sut.GetEntriesSince(cursor));

        sut.AddEntry(MakeEntry(5, text: "m5"));
        var next = sut.GetEntriesSince(cursor);

        Assert.Equal("m5", Assert.Single(next).Text);
    }

    // ── WaitForQuietAsync (stream-completion detection) ──

    [Fact]
    public async Task WaitForQuietAsync_returns_true_on_silent_buffer()
    {
        var sut = new DataBufferService();

        var quiet = await sut.WaitForQuietAsync(quietMs: 60, timeoutMs: 5000);

        Assert.True(quiet);
    }

    [Fact]
    public async Task WaitForQuietAsync_activity_extends_quiet_window()
    {
        // A mid-wait entry must reset the quiet window: with quietMs=200 and an
        // entry arriving ~100ms in, a true before ~300ms would mean the activity
        // was ignored.
        var sut = new DataBufferService();
        var start = Environment.TickCount64;

        var wait = sut.WaitForQuietAsync(quietMs: 200, timeoutMs: 5000);
        await Task.Delay(100);
        sut.AddEntry(MakeEntry(1, text: "still streaming"));

        var quiet = await wait;
        var elapsed = Environment.TickCount64 - start;

        Assert.True(quiet);
        Assert.True(elapsed >= 250, $"returned after {elapsed}ms — activity did not reset the quiet window");
    }

    [Fact]
    public async Task WaitForQuietAsync_returns_false_when_traffic_never_goes_quiet()
    {
        var sut = new DataBufferService();
        using var cts = new CancellationTokenSource();

        // Feed an entry every 30ms so a 150ms quiet window can never be reached.
        var feeder = Task.Run(async () =>
        {
            int id = 0;
            while (!cts.IsCancellationRequested)
            {
                sut.AddEntry(MakeEntry(++id, text: "tick"));
                await Task.Delay(30, CancellationToken.None);
            }
        });

        var quiet = await sut.WaitForQuietAsync(quietMs: 150, timeoutMs: 400);

        cts.Cancel();
        await feeder;

        Assert.False(quiet);
    }

    [Fact]
    public async Task WaitForQuietAsync_activity_after_timeout_call_still_observed()
    {
        // Buffer that goes silent only after one more entry: quiet detection is
        // based on seq change, not on buffer emptiness.
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, text: "old"));

        var wait = sut.WaitForQuietAsync(quietMs: 80, timeoutMs: 5000);
        await Task.Delay(30);
        sut.AddEntry(MakeEntry(2, text: "new"));

        var quiet = await wait;

        Assert.True(quiet);
        Assert.Equal(2, sut.Count());
    }

    // ── WaitEntriesSinceAsync (long-poll cursor read) ──

    [Fact]
    public async Task WaitEntriesSinceAsync_returns_existing_data_immediately()
    {
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, text: "already"));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (entries, maxId) = await sut.WaitEntriesSinceAsync(0, null, 100, waitMs: 5000);
        sw.Stop();

        Assert.Equal("already", Assert.Single(entries).Text);
        Assert.Equal(1, maxId);
        Assert.True(sw.ElapsedMilliseconds < 2000, $"waited {sw.ElapsedMilliseconds}ms despite data");
    }

    [Fact]
    public async Task WaitEntriesSinceAsync_wakes_on_arrival()
    {
        var sut = new DataBufferService();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var waitTask = sut.WaitEntriesSinceAsync(0, null, 100, waitMs: 5000);
        await Task.Delay(50);
        sut.AddEntry(MakeEntry(1, text: "arrived"));
        var (entries, maxId) = await waitTask;
        sw.Stop();

        Assert.Equal("arrived", Assert.Single(entries).Text);
        Assert.Equal(1, maxId);
        Assert.True(sw.ElapsedMilliseconds < 4000, $"took {sw.ElapsedMilliseconds}ms — arrival signal lost");
    }

    [Fact]
    public async Task WaitEntriesSinceAsync_timeout_returns_empty_with_unchanged_cursor()
    {
        var sut = new DataBufferService();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (entries, maxId) = await sut.WaitEntriesSinceAsync(7, null, 100, waitMs: 200);
        sw.Stop();

        Assert.Empty(entries);
        Assert.Equal(7, maxId); // cursor must not move without data
        Assert.True(sw.ElapsedMilliseconds >= 150, $"returned after {sw.ElapsedMilliseconds}ms — wait not honored");
    }

    [Fact]
    public async Task WaitEntriesSinceAsync_nonpositive_wait_behaves_like_plain_read()
    {
        var sut = new DataBufferService();

        var (entries, maxId) = await sut.WaitEntriesSinceAsync(0, null, 100, waitMs: 0);

        Assert.Empty(entries);
        Assert.Equal(0, maxId);
    }

    [Fact]
    public async Task WaitEntriesSinceAsync_no_lost_wakeup_across_registration_race()
    {
        // Registration runs synchronously before the first await, so an add
        // right after the call must either be seen by the pre-registration
        // seq check or released by the arrival signal — never lost. Stress the
        // boundary repeatedly.
        for (int i = 0; i < 50; i++)
        {
            var sut = new DataBufferService();
            var waitTask = sut.WaitEntriesSinceAsync(0, null, 10, waitMs: 2000);
            sut.AddEntry(MakeEntry(1, text: $"race{i}"));

            var (entries, _) = await waitTask;
            Assert.Single(entries);
        }
    }

    [Fact]
    public async Task WaitEntriesSinceAsync_concurrent_long_pollers_all_wake()
    {
        var sut = new DataBufferService();
        var waits = new Task<(List<LogEntry> Entries, int ScannedMaxId)>[10];
        for (int i = 0; i < waits.Length; i++)
            waits[i] = sut.WaitEntriesSinceAsync(0, null, 10, waitMs: 5000);

        sut.AddEntry(MakeEntry(1, text: "fanout"));

        var results = await Task.WhenAll(waits);
        Assert.All(results, r => Assert.Equal("fanout", Assert.Single(r.Entries).Text));
    }
}
