using System;
using System.Collections.Generic;

namespace ACCcom.Core.Services;

/// <summary>
/// Default-name generation for user-created rules (highlight rules, trigger
/// rules). Both editors used to stamp "Rule_{Count+1}" inline, which collides
/// after a middle rule is deleted — HighlightService.AddRule replaces by name,
/// so a colliding default silently overwrote an existing rule. Names are now
/// made unique against the live rule set, keeping the "Rule_" prefix the
/// highlight editor's cancel-rollback logic relies on.
/// </summary>
public static class RuleNaming
{
    /// <summary>
    /// Returns a fresh default rule name of the form "Rule_1", "Rule_2", ...
    /// that is not present in <paramref name="existingNames"/> (ordinal
    /// comparison), skipping any number that is already taken.
    /// </summary>
    public static string NextName(IEnumerable<string> existingNames)
    {
        ArgumentNullException.ThrowIfNull(existingNames);

        var used = new HashSet<string>(existingNames, StringComparer.Ordinal);
        int n = 1;
        string candidate;
        do { candidate = $"Rule_{n++}"; } while (used.Contains(candidate));
        return candidate;
    }
}