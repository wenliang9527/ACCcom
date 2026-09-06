using System.Globalization;
using System.Text.RegularExpressions;

namespace ACCcom.Core.Services;

/// <summary>
/// Extracts numeric values from serial text for the plot window. First pass
/// scans key=value / key:value segments (so mixed text like "temp=23.5 ok"
/// yields 23.5); if nothing matched, a fallback pass picks up standalone
/// decimals ("rx 12.3" / "-4.5"). Extracted from MainViewModel so the
/// regex/try-parse rules are unit-testable. Values are parsed invariant
/// (decimal point is '.', never the locale separator).
/// </summary>
public static class NumericValueExtractor
{
    // Optional [=:] + whitespace, then an optional sign and digits with an
    // optional decimal part. Group 1 captures the number.
    private static readonly Regex KeyValueRegex = new(@"[=:]?\s*(-?\d+\.?\d*)", RegexOptions.Compiled);
    // Standalone decimals (require the fraction part so they don't collide
    // with IDs / counts).
    private static readonly Regex StandaloneNumberRegex = new(@"-?\d+\.\d+", RegexOptions.Compiled);

    /// <summary>Returns the numbers found in <paramref name="text"/>, or an
    /// empty list for empty/whitespace input.</summary>
    public static List<double> Extract(string text)
    {
        var results = new List<double>();
        if (string.IsNullOrWhiteSpace(text)) return results;

        foreach (Match m in KeyValueRegex.Matches(text))
        {
            if (double.TryParse(m.Groups[1].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double val))
                results.Add(val);
        }

        if (results.Count == 0)
        {
            foreach (Match m in StandaloneNumberRegex.Matches(text))
            {
                if (double.TryParse(m.Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double val))
                    results.Add(val);
            }
        }

        return results;
    }
}