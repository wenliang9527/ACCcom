namespace ACCcom.Core.Services;

/// <summary>
/// Helper for the trigger SaveToFile action. Kept in Core (not in the WPF
/// ViewModel layer) so unit tests can exercise the path-resolution rules
/// without taking a dependency on the UI assembly.
/// </summary>
public static class TriggerPathResolver
{
    /// <summary>Directory the SaveToFile action resolves relative paths against.
    /// Keeping it under %LOCALAPPDATA% means users can write `payloads.log`
    /// without inventing a path, and the file survives reinstalls.</summary>
    public static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ACCcom", "triggers");

    /// <summary>Resolves a trigger action-parameter path. Absolute paths are
    /// honoured as-is; relative paths are anchored at <see cref="DataDirectory"/>
    /// so users don't have to invent a path every time. Empty / whitespace-only
    /// input is returned as an empty string so callers can short-circuit.</summary>
    public static string Resolve(string? path)
    {
        return TryResolve(path, out var resolved) ? resolved : "";
    }

    /// <summary>Tries to resolve <paramref name="path"/> without throwing.
    /// Relative paths that would escape <see cref="DataDirectory"/> (e.g.
    /// `../../evil.log`) are rejected with false.</summary>
    public static bool TryResolve(string? path, out string resolved)
    {
        resolved = "";
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Path.IsPathRooted(path)) { resolved = path; return true; }
        var combined = Path.GetFullPath(Path.Combine(DataDirectory, path));
        var baseFull = Path.GetFullPath(DataDirectory);
        if (!combined.StartsWith(baseFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(combined, baseFull, StringComparison.OrdinalIgnoreCase))
            return false;
        resolved = combined;
        return true;
    }
}
