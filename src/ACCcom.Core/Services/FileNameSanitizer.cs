namespace ACCcom.Core.Services;

/// <summary>
/// Replaces characters that are invalid in a file name with '_' and falls back
/// to a default name when the result is empty/whitespace. Used to derive a safe
/// on-disk file name from a user-typed script name. Extracted from
/// ProtocolTestViewModel so the sanitization rules are unit-testable.
/// </summary>
public static class FileNameSanitizer
{
    /// <summary>Sanitizes <paramref name="name"/> for use as a file name.
    /// Returns "script" when the input is empty/whitespace or sanitizes to
    /// nothing.</summary>
    public static string Sanitize(string? name)
    {
        var safe = name ?? "";
        foreach (var c in Path.GetInvalidFileNameChars())
            safe = safe.Replace(c, '_');
        return string.IsNullOrWhiteSpace(safe) ? "script" : safe;
    }
}
