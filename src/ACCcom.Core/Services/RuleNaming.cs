using System;
using System.Collections.Generic;

namespace ACCcom.Core.Services;

/// <summary>
/// Default-name generation for user-created items (rules, test steps, schema
/// fields). Editors used to stamp "&lt;Prefix>{Count+1}" inline, which collides
/// after a middle item is deleted — HighlightService.AddRule replaces by name,
/// so a colliding default silently overwrote an existing rule. Names are now
/// made unique against the live set, keeping the "Rule_" prefix the highlight
/// editor's cancel-rollback logic relies on.
/// </summary>
public static class RuleNaming
{
    /// <summary>
    /// Returns a fresh default name of the form "Rule_1", "Rule_2", ...
    /// that is not present in <paramref name="existingNames"/> (ordinal
    /// comparison), skipping any number that is already taken.
    /// </summary>
    public static string NextName(IEnumerable<string> existingNames)
        => NextName(existingNames, "Rule_");

    /// <summary>
    /// Returns a fresh default name of the form "&lt;prefix>1", "&lt;prefix>2",
    /// ... that is not present in <paramref name="existingNames"/> (ordinal
    /// comparison). <paramref name="prefix"/> may be empty.
    /// </summary>
    public static string NextName(IEnumerable<string> existingNames, string prefix)
    {
        ArgumentNullException.ThrowIfNull(existingNames);
        ArgumentNullException.ThrowIfNull(prefix);

        var used = new HashSet<string>(existingNames, StringComparer.Ordinal);
        int n = 1;
        string candidate;
        do { candidate = $"{prefix}{n++}"; } while (used.Contains(candidate));
        return candidate;
    }
}