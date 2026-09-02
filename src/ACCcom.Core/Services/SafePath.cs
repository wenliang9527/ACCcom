using System.IO;

namespace ACCcom.Core.Services;

/// <summary>
/// Central validation for user-supplied file names coming from remote
/// entry points (HTTP API / MCP tools). Prevents path traversal by
/// rejecting separators, rooted paths and parent-directory segments,
/// and by verifying the resolved path stays inside the base directory.
/// </summary>
public static class SafePath
{
    /// <summary>
    /// Combines <paramref name="fileName"/> under <paramref name="baseDir"/> after
    /// verifying it is a plain file name (no directory components).
    /// </summary>
    public static bool TryCombineUnder(string baseDir, string? fileName, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(baseDir) || string.IsNullOrWhiteSpace(fileName))
            return false;

        if (!IsPlainFileName(fileName))
            return false;

        try
        {
            var combined = Path.Combine(baseDir, fileName);
            var fullBase = Path.GetFullPath(baseDir);
            var fullCombined = Path.GetFullPath(combined);
            if (!fullCombined.StartsWith(fullBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fullCombined, fullBase, StringComparison.OrdinalIgnoreCase))
                return false;

            fullPath = fullCombined;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>True when the name contains no path/rooting syntax and has no invalid file name chars.</summary>
    public static bool IsPlainFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Contains('/') || name.Contains('\\')) return false;
        if (name.Contains(':')) return false;
        if (name == "." || name == "..") return false;
        if (name.Contains('\0')) return false;
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            if (name.Contains(c)) return false;
        }
        return true;
    }

    /// <summary>
    /// Resolves an optional recording file name against the default recordings
    /// directory. Returns false when the supplied name escapes that directory.
    /// </summary>
    public static bool TryResolveRecordingPath(string? filename, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(filename))
            return false;

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ACCcom", "recordings");
        return TryCombineUnder(dir, filename, out fullPath);
    }
}
