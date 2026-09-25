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

        var span = text.AsSpan();

        // Both patterns require at least one ASCII digit, so a text frame with
        // none ("OK", "ERR", hex dumps without digits) can skip both regex
        // passes entirely — this is the common case for status-only traffic.
        bool hasDigit = false;
        foreach (var c in span)
        {
            if (c >= '0' && c <= '9') { hasDigit = true; break; }
        }
        if (!hasDigit) return results;

        // EnumerateMatches yields span matches with no Match/MatchCollection
        // allocations — the plot path runs this per RX entry while open.
        foreach (var m in KeyValueRegex.EnumerateMatches(span))
        {
            // Group 1 is the whole match minus the optional "[=:]?\s*" prefix,
            // which only ever contains '=', ':' and whitespace.
            var number = span.Slice(m.Index, m.Length);
            int i = 0;
            while (i < number.Length &&
                   (number[i] == '=' || number[i] == ':' || char.IsWhiteSpace(number[i])))
                i++;
            if (i >= number.Length) continue;
            if (double.TryParse(number[i..], NumberStyles.Float,
                CultureInfo.InvariantCulture, out double val))
                results.Add(val);
        }

        if (results.Count == 0)
        {
            foreach (var m in StandaloneNumberRegex.EnumerateMatches(span))
            {
                if (double.TryParse(span.Slice(m.Index, m.Length), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double val))
                    results.Add(val);
            }
        }

        return results;
    }
}
