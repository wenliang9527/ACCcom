using System;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

/// <summary>
/// Quick-send shortcut list filter: case-insensitive substring match on the
/// command name, with an empty filter matching everything. Extracted from
/// ShortcutViewModel so the visible-command semantics are unit-testable
/// without the UI layer.
/// </summary>
public static class ShortcutFilter
{
    /// <summary>True when <paramref name="item"/> should be visible under the
    /// given filter. An empty/whitespace filter shows all commands; a non-empty
    /// filter keeps commands whose name contains it (ordinal, case-insensitive),
    /// mirroring the search box behaviour.</summary>
    public static bool IsMatch(ShortcutItem item, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        return item.Name != null
            && item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}