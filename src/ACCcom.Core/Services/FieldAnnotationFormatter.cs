using System;
using System.Collections.Generic;
using System.Text;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

/// <summary>
/// Formats parsed <see cref="FieldAnnotation"/> lists into the human-readable
/// preview text shown in the schema editor's "test parse" panel. Pulled out of
/// the view model so the severity markers and column layout are unit-testable
/// without a UI thread.
/// </summary>
public static class FieldAnnotationFormatter
{
    /// <summary>One line per field, severity-marked and column-aligned, e.g.
    /// "✓ [00] 帧头       0102    01 02".</summary>
    public static string Format(IEnumerable<FieldAnnotation>? fields)
    {
        if (fields == null) return "";

        var sb = new StringBuilder();
        foreach (var field in fields)
        {
            var sev = field.Severity switch
            {
                FieldSeverity.Warning => "⚠",
                FieldSeverity.Error => "✗",
                _ => "✓"
            };
            sb.AppendLine($"{sev} [{field.Offset:X2}] {field.Name,-10} {field.RawHex,-8} {field.DisplayValue}");
        }
        return sb.ToString();
    }
}