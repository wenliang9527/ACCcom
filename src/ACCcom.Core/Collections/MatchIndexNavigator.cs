using System;
using System.Collections.Generic;

namespace ACCcom.Core.Collections;

/// <summary>
/// Pure stepping logic for "jump to next/previous filter match" navigation.
/// Scans the source twice without materializing intermediate lists, so F3
/// navigation never allocates on top of the collection view.
/// </summary>
public static class MatchIndexNavigator
{
    /// <summary>
    /// Returns the item the selection should move to when stepping through
    /// <paramref name="source"/> over entries accepted by <paramref name="isMatch"/>,
    /// or <see langword="null"/> when there is no move to make (no matches, or the
    /// current item is already at the requested end).
    /// </summary>
    public static T? Step<T>(IEnumerable<T> source, Func<T, bool> isMatch, T? current, bool forward)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(isMatch);

        T? lastMatch = null;
        int currentPos = -1;
        int matchCount = 0;
        foreach (var item in source)
        {
            if (!isMatch(item)) continue;
            if (ReferenceEquals(item, current)) currentPos = matchCount;
            lastMatch = item;
            matchCount++;
        }

        if (matchCount == 0) return null;

        int nextPos = forward
            ? (currentPos < 0 ? 0 : Math.Min(currentPos + 1, matchCount - 1))
            : (currentPos <= 0 ? 0 : currentPos - 1);

        int pos = 0;
        foreach (var item in source)
        {
            if (!isMatch(item)) continue;
            if (pos == nextPos) return ReferenceEquals(item, current) ? null : item;
            pos++;
        }
        return null;
    }
}