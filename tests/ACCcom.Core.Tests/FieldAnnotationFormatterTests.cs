using System.Collections.Generic;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class FieldAnnotationFormatterTests
{
    [Fact]
    public void Format_EmitsSeverityMarkers()
    {
        var fields = new List<FieldAnnotation>
        {
            new() { Name = "ok", Offset = 0, RawHex = "01", DisplayValue = "1", Severity = FieldSeverity.Normal },
            new() { Name = "warn", Offset = 1, RawHex = "02", DisplayValue = "2", Severity = FieldSeverity.Warning },
            new() { Name = "err", Offset = 2, RawHex = "03", DisplayValue = "3", Severity = FieldSeverity.Error }
        };

        var text = FieldAnnotationFormatter.Format(fields);

        Assert.StartsWith("✓ [00] ok", text);
        Assert.Contains("⚠ [01] warn", text);
        Assert.Contains("✗ [02] err", text);
    }

    [Fact]
    public void Format_ColumnsAlignedAndOffsetUpperCaseHex()
    {
        var fields = new List<FieldAnnotation>
        {
            new() { Name = "a", Offset = 0x0A, RawHex = "ABCD", DisplayValue = "x" }
        };

        var text = FieldAnnotationFormatter.Format(fields);

        Assert.Contains("[0A]", text);
        Assert.Contains("ABCD", text);
    }

    [Fact]
    public void Format_Null_ReturnsEmpty()
    {
        Assert.Equal("", FieldAnnotationFormatter.Format(null));
    }

    [Fact]
    public void Format_EmptyList_ReturnsEmpty()
    {
        Assert.Equal("", FieldAnnotationFormatter.Format(new List<FieldAnnotation>()));
    }
}