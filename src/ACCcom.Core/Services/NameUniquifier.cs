using System;
using System.Collections.Generic;
using System.Linq;

namespace ACCcom.Core.Services;

/// <summary>
/// Pure naming helper that makes a user-supplied name unique against a set of
/// existing names: "MyPage" stays "MyPage" when free, otherwise it becomes
/// "MyPage (2)", "MyPage (3)", ... (ordinal comparison). Pulled out of the
/// shortcut view model so the merge-import rename policy is unit-testable
/// without a UI thread.
/// </summary>
public static class NameUniquifier
{
    /// <summary>
    /// Returns <paramref name="baseName"/> when it is not present in
    /// <paramref name="existingNames"/>; otherwise returns the first
    /// "&lt;baseName&gt; (N)" variant (N starting at 2) that is free.
    /// </summary>
    public static string UniqueName(IEnumerable<string> existingNames, string baseName)
    {
        ArgumentNullException.ThrowIfNull(existingNames);
        ArgumentNullException.ThrowIfNull(baseName);

        if (baseName.Length == 0) return baseName;
        if (!existingNames.Any(n => string.Equals(n, baseName, StringComparison.Ordinal)))
            return baseName;

        var used = new HashSet<string>(existingNames, StringComparer.Ordinal);
        for (int i = 2; ; i++)
        {
            var candidate = $"{baseName} ({i})";
            if (!used.Contains(candidate)) return candidate;
        }
    }
}