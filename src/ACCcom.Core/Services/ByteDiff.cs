using System;

namespace ACCcom.Core.Services;

/// <summary>
/// Positional byte-by-byte comparison of two frames, used by the DiffWindow
/// to highlight per-byte matches/differences. Pure computation (no WPF
/// dependency) so the per-byte classification and diff count are unit-testable.
/// </summary>
public static class ByteDiff
{
    /// <summary>Per-byte classification: matched (both sides, equal), or a
    /// mismatch (different values, or one side shorter than the other).</summary>
    public enum ByteState
    {
        Match,
        Diff
    }

    /// <summary>
    /// Compares two frames byte by byte. Returns one state per position up to
    /// the longer frame's length; a byte present on only one side counts as a
    /// diff. <paramref name="diffCount"/> is the number of differing positions.
    /// </summary>
    public static (ByteState[] States, int DiffCount) Compare(byte[] a, byte[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        int maxLen = Math.Max(a.Length, b.Length);
        var states = new ByteState[maxLen];
        int diffCount = 0;

        for (int i = 0; i < maxLen; i++)
        {
            bool hasA = i < a.Length;
            bool hasB = i < b.Length;
            bool same = hasA && hasB && a[i] == b[i];
            states[i] = same ? ByteState.Match : ByteState.Diff;
            if (!same) diffCount++;
        }

        return (states, diffCount);
    }
}