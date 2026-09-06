namespace ACCcom.Core.Services;

/// <summary>
/// Parses/serializes the enum/bitfield value map of a schema field — a
/// comma-separated "key=value,key=value" string held by the schema editor.
/// "key" is the enum name, "value" the numeric value. Segments without an
/// equals sign are skipped (they are typically trailing user typos).
/// Extracted from FieldItemViewModel so the parse/serialize round-trip is
/// unit-testable without the UI layer.
/// </summary>
public static class FieldValueMap
{
    /// <summary>Parses a comma-separated "k=v,k=v" string into a dictionary.
    /// Returns null for empty/whitespace input. Segments lacking an '=' are
    /// silently skipped; keys are trimmed and empty keys ignored.</summary>
    public static Dictionary<string, string>? Parse(string? valuesText)
    {
        if (string.IsNullOrWhiteSpace(valuesText))
            return null;

        var dict = new Dictionary<string, string>();
        var pairs = valuesText.Split(',');
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=');
            if (parts.Length == 2)
            {
                var key = parts[0].Trim();
                var value = parts[1].Trim();
                if (!string.IsNullOrEmpty(key))
                    dict[key] = value;
            }
        }
        return dict.Count > 0 ? dict : null;
    }

    /// <summary>Serializes a dictionary back to "k=v,k=v" form; empty string
    /// for null/empty input.</summary>
    public static string Serialize(Dictionary<string, string>? values)
    {
        if (values == null || values.Count == 0)
            return "";
        return string.Join(",", values.Select(kv => $"{kv.Key}={kv.Value}"));
    }
}