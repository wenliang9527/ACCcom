using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class EntryListTrimmerTests
{
    [Fact]
    public void ComputeRemoveCount_no_overflow_returns_zero()
    {
        Assert.Equal(0, EntryListTrimmer.ComputeRemoveCount(100, maxEntries: 100, chunkSize: 100));
        Assert.Equal(0, EntryListTrimmer.ComputeRemoveCount(99, maxEntries: 100, chunkSize: 100));
    }

    [Fact]
    public void ComputeRemoveCount_rounds_up_to_chunk_boundary()
    {
        // Overflow 1 with chunk 100: rounds to 100 (whole chunk).
        Assert.Equal(100, EntryListTrimmer.ComputeRemoveCount(101, maxEntries: 100, chunkSize: 100));
        // Overflow 50: rounds to 100.
        Assert.Equal(100, EntryListTrimmer.ComputeRemoveCount(150, maxEntries: 100, chunkSize: 100));
        // Overflow 100 exactly: stays 100.
        Assert.Equal(100, EntryListTrimmer.ComputeRemoveCount(200, maxEntries: 100, chunkSize: 100));
        // Overflow 101: rounds to 200.
        Assert.Equal(200, EntryListTrimmer.ComputeRemoveCount(201, maxEntries: 100, chunkSize: 100));
    }

    [Fact]
    public void ComputeRemoveCount_never_exceeds_count()
    {
        // Count 5, overflow 3: ceil(3/100)=1 chunk=100, clamped to 5.
        Assert.Equal(5, EntryListTrimmer.ComputeRemoveCount(5, maxEntries: 2, chunkSize: 100));
        // Count 1, overflow 1: clamped to 1.
        Assert.Equal(1, EntryListTrimmer.ComputeRemoveCount(1, maxEntries: 0, chunkSize: 100));
    }

    [Fact]
    public void ComputeRemoveCount_small_chunk()
    {
        // Chunk 10, count 15, cap 10: overflow 5 rounds to 10.
        Assert.Equal(10, EntryListTrimmer.ComputeRemoveCount(15, maxEntries: 10, chunkSize: 10));
    }

    [Fact]
    public void ComputeRemoveCount_zero_cap_removes_everything()
    {
        // Overflow = count - 0 = count; rounded up may exceed count, clamped to count.
        Assert.Equal(7, EntryListTrimmer.ComputeRemoveCount(7, maxEntries: 0, chunkSize: 100));
    }

    [Fact]
    public void Trim_removes_computed_oldest()
    {
        var list = Enumerable.Range(1, 15).Select(i => $"e{i}").ToList();

        EntryListTrimmer.Trim(list, maxEntries: 10, chunkSize: 10);

        // Rounds to 10 removed: leaves the newest 5.
        Assert.Equal(new[] { "e11", "e12", "e13", "e14", "e15" }, list);
    }

    [Fact]
    public void Trim_no_op_when_within_capacity()
    {
        var list = new List<string> { "a", "b" };

        EntryListTrimmer.Trim(list, maxEntries: 10, chunkSize: 10);

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void Trim_removes_all_when_count_smaller_than_chunk()
    {
        var list = new List<string> { "a", "b", "c" };

        EntryListTrimmer.Trim(list, maxEntries: 0, chunkSize: 100);

        Assert.Empty(list);
    }

    [Fact]
    public void ComputeRemoveCount_nonPositive_chunk_clamped_to_one()
    {
        // A zero/negative chunk used to divide by zero (or garbage); it now
        // behaves as chunk 1 (trim one entry at a time).
        Assert.Equal(3, EntryListTrimmer.ComputeRemoveCount(5, maxEntries: 2, chunkSize: 0));
        Assert.Equal(3, EntryListTrimmer.ComputeRemoveCount(5, maxEntries: 2, chunkSize: -10));
        Assert.Equal(1, EntryListTrimmer.ComputeRemoveCount(1, maxEntries: 0, chunkSize: 0));
    }

    [Fact]
    public void Trim_nonPositive_chunk_removes_all_overflow()
    {
        var list = new List<string> { "a", "b", "c", "d", "e" };

        EntryListTrimmer.Trim(list, maxEntries: 2, chunkSize: 0);

        Assert.Equal(2, list.Count);
        Assert.Equal(new[] { "d", "e" }, list);
    }

    [Fact]
    public void ComputeRemoveCount_matches_original_semantics()
    {
        // Reproduce the pre-extraction formula for a range of inputs to ensure
        // the extraction did not change behavior.
        for (int count = 0; count <= 500; count += 7)
        {
            for (int cap = 0; cap <= 300; cap += 13)
            {
                var overflow = count - cap;
                var expected = overflow <= 0 ? 0 : Math.Min(((overflow + 99) / 100) * 100, count);
                Assert.Equal(expected, EntryListTrimmer.ComputeRemoveCount(count, cap, 100));
            }
        }
    }
}
