namespace ACCcom.Core.Services;

/// <summary>
/// Trim helper for the bounded display buffers: given a collection count and
/// the max-entry cap, computes how many oldest entries to drop so the collection
/// lands back under the cap. The count rounds up to a chunk boundary so a
/// range-removal implementation (ObservableRangeCollection) fires one
/// CollectionChanged notification per chunk instead of one per entry, while
/// never exceeding the actual collection size. Extracted from
/// DataFlowViewModel.TrimBuffer so the rounding/clamp edge cases are locked.
/// </summary>
public static class EntryListTrimmer
{
    /// <summary>Number of entries to remove from the front to bring
    /// <paramref name="count"/> down to at most <paramref name="maxEntries"/>,
    /// rounded up to a multiple of <paramref name="chunkSize"/>. Returns 0 when
    /// there is nothing to trim, and never returns more than
    /// <paramref name="count"/>.</summary>
    public static int ComputeRemoveCount(int count, int maxEntries, int chunkSize)
    {
        var overflow = count - maxEntries;
        if (overflow <= 0) return 0;

        // A non-positive chunk size would divide by zero (or produce a garbage
        // result); clamp to 1 so trimming still happens in single-entry steps
        // (the caller's const chunk is positive, this defends misuse).
        if (chunkSize <= 0) chunkSize = 1;

        // ceil(overflow / chunkSize) * chunkSize — round up so the removal
        // happens in whole chunks, then clamp to the collection size.
        var rounded = ((overflow + chunkSize - 1) / chunkSize) * chunkSize;
        return Math.Min(rounded, count);
    }

    /// <summary>Removes the computed number of oldest entries from
    /// <paramref name="entries"/> in place (front of the list).</summary>
    public static void Trim<T>(IList<T> entries, int maxEntries, int chunkSize)
    {
        var removeCount = ComputeRemoveCount(entries.Count, maxEntries, chunkSize);
        if (removeCount <= 0) return;
        for (int i = 0; i < removeCount; i++)
            entries.RemoveAt(0);
    }
}
