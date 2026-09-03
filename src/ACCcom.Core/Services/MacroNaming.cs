using System;
using System.Collections.Generic;
using System.Linq;

namespace ACCcom.Core.Services;

/// <summary>
/// Pure naming helper for user-created macros. Picks the first free "&lt;prefix> n"
/// name so new macros never collide with existing ones.
/// </summary>
public static class MacroNaming
{
    /// <summary>
    /// Returns the first name of the form "<paramref name="prefix"/> N" that is not
    /// present in <paramref name="existingNames"/> (ordinal comparison), starting at 1.
    /// </summary>
    public static string NextName(IEnumerable<string> existingNames, string prefix = "Macro")
    {
        ArgumentNullException.ThrowIfNull(existingNames);
        ArgumentNullException.ThrowIfNull(prefix);

        var used = new HashSet<string>(existingNames, StringComparer.Ordinal);
        int n = 1;
        string candidate;
        do { candidate = $"{prefix} {n++}"; } while (used.Contains(candidate));
        return candidate;
    }
}