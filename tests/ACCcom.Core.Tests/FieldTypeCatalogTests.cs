using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class FieldTypeCatalogTests
{
    [Fact]
    public void FieldTypes_contains_all_editor_types()
    {
        var expected = new[]
        {
            "uint8", "uint16", "uint32", "int8", "int16", "int32", "float", "double",
            "string", "hex", "bcd", "enum", "bitfield"
        };

        Assert.Equal(expected, FieldTypeCatalog.FieldTypes);
    }

    [Fact]
    public void ChecksumTypes_contains_all_editor_types()
    {
        Assert.Equal(new[] { "none", "xor8", "sum8", "crc16" }, FieldTypeCatalog.ChecksumTypes);
    }

    [Theory]
    [InlineData("uint8", true)]
    [InlineData("double", true)]
    [InlineData("bitfield", true)]
    [InlineData("UINT8", false)]     // case-sensitive
    [InlineData("", false)]
    [InlineData("custom", false)]
    public void IsValidFieldType(string type, bool expected)
    {
        Assert.Equal(expected, FieldTypeCatalog.IsValidFieldType(type));
    }

    [Theory]
    [InlineData("none", true)]
    [InlineData("crc16", true)]
    [InlineData("md5", false)]
    public void IsValidChecksumType(string type, bool expected)
    {
        Assert.Equal(expected, FieldTypeCatalog.IsValidChecksumType(type));
    }

    [Theory]
    [InlineData("uint8", 1)]
    [InlineData("int8", 1)]
    [InlineData("bcd", 1)]
    [InlineData("hex", 1)]
    [InlineData("uint16", 2)]
    [InlineData("int16", 2)]
    [InlineData("uint32", 4)]
    [InlineData("int32", 4)]
    [InlineData("float", 4)]
    [InlineData("double", 8)]
    public void GetFixedLength_numeric_types(string type, int expected)
    {
        Assert.Equal(expected, FieldTypeCatalog.GetFixedLength(type));
    }

    [Theory]
    [InlineData("string")]
    [InlineData("enum")]
    [InlineData("bitfield")]
    public void GetFixedLength_variable_types_default_to_one(string type)
    {
        // The editor resets Length to 1 when switching to a variable-size type.
        Assert.Equal(1, FieldTypeCatalog.GetFixedLength(type));
    }

    [Theory]
    [InlineData("custom")]
    [InlineData("")]
    [InlineData(null)]
    public void GetFixedLength_unknown_type_returns_null(string? type)
    {
        Assert.Null(FieldTypeCatalog.GetFixedLength(type!));
    }
}
