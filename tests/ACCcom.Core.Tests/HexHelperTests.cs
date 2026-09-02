using ACCcom.Core.Models;

namespace ACCcom.Core.Tests;

public class HexHelperTests
{
    [Fact]
    public void BytesToHexSpaced_EmptyCount_ReturnsEmpty()
    {
        var result = HexHelper.BytesToHexSpaced([0xAA, 0x55], 0, 0);
        Assert.Equal("", result);
    }

    [Fact]
    public void BytesToHexSpaced_SingleByte_ReturnsHex()
    {
        var result = HexHelper.BytesToHexSpaced([0xAB], 0, 1);
        Assert.Equal("AB", result);
    }

    [Fact]
    public void BytesToHexSpaced_MultipleBytes_ReturnsSpaced()
    {
        var result = HexHelper.BytesToHexSpaced([0xAA, 0x55, 0x03], 0, 3);
        Assert.Equal("AA 55 03", result);
    }

    [Fact]
    public void BytesToHexSpaced_WithOffset_ReturnsPartial()
    {
        var result = HexHelper.BytesToHexSpaced([0x00, 0xAA, 0x55, 0x03], 1, 2);
        Assert.Equal("AA 55", result);
    }

    [Fact]
    public void BytesToHexSpaced_LargeBuffer()
    {
        var bytes = new byte[256];
        for (int i = 0; i < 256; i++)
            bytes[i] = (byte)i;

        var result = HexHelper.BytesToHexSpaced(bytes, 0, 256);
        var parts = result.Split(' ');
        Assert.Equal(256, parts.Length);
        Assert.Equal("00", parts[0]);
        Assert.Equal("FF", parts[255]);
    }

    [Fact]
    public void BytesToHexSpaced_ZeroBytes()
    {
        var result = HexHelper.BytesToHexSpaced([0x00], 0, 1);
        Assert.Equal("00", result);
    }

    // ========== CountHexBytes ==========

    [Fact]
    public void CountHexBytes_EmptyString_ReturnsZero()
    {
        Assert.Equal(0, HexHelper.CountHexBytes(""));
    }

    [Fact]
    public void CountHexBytes_SingleByte_ReturnsOne()
    {
        Assert.Equal(1, HexHelper.CountHexBytes("AA"));
    }

    [Fact]
    public void CountHexBytes_SpacedHex_ReturnsCorrectCount()
    {
        Assert.Equal(3, HexHelper.CountHexBytes("AA BB CC"));
    }

    [Fact]
    public void CountHexBytes_MultipleSpaces_ReturnsCorrectCount()
    {
        Assert.Equal(3, HexHelper.CountHexBytes("AA   BB   CC"));
    }

    // ========== HexStringToBytes ==========

    [Fact]
    public void HexStringToBytes_EmptyString_ReturnsEmpty()
    {
        Assert.Empty(HexHelper.HexStringToBytes(""));
    }

    [Fact]
    public void HexStringToBytes_SpacedHex_ReturnsCorrectBytes()
    {
        var result = HexHelper.HexStringToBytes("AA BB CC");
        Assert.Equal([0xAA, 0xBB, 0xCC], result);
    }

    [Fact]
    public void HexStringToBytes_UnspacedHex_ReturnsCorrectBytes()
    {
        var result = HexHelper.HexStringToBytes("AABBCC");
        Assert.Equal([0xAA, 0xBB, 0xCC], result);
    }

    [Fact]
    public void HexStringToBytes_MixedCase_ReturnsCorrectBytes()
    {
        var result = HexHelper.HexStringToBytes("aA bB cC");
        Assert.Equal([0xAA, 0xBB, 0xCC], result);
    }

    [Fact]
    public void HexStringToBytes_SingleNibble_ReturnsEmpty()
    {
        Assert.Empty(HexHelper.HexStringToBytes("A"));
    }

    [Fact]
    public void HexStringToBytes_WithInvalidChars_ReplacesWithZero()
    {
        var result = HexHelper.HexStringToBytes("XZ YY");
        Assert.Equal([0x00, 0x00], result);
    }

    // ========== HasErrorSeverity ==========

    [Fact]
    public void HasErrorSeverity_NullFields_ReturnsFalse()
    {
        Assert.False(HexHelper.HasErrorSeverity(null));
    }

    [Fact]
    public void HasErrorSeverity_EmptyFields_ReturnsFalse()
    {
        Assert.False(HexHelper.HasErrorSeverity([]));
    }

    [Fact]
    public void HasErrorSeverity_NoErrorSeverity_ReturnsFalse()
    {
        var fields = new List<FieldAnnotation>
        {
            new() { Severity = FieldSeverity.Normal },
            new() { Severity = FieldSeverity.Warning }
        };
        Assert.False(HexHelper.HasErrorSeverity(fields));
    }

    [Fact]
    public void HasErrorSeverity_HasErrorSeverity_ReturnsTrue()
    {
        var fields = new List<FieldAnnotation>
        {
            new() { Severity = FieldSeverity.Normal },
            new() { Severity = FieldSeverity.Error }
        };
        Assert.True(HexHelper.HasErrorSeverity(fields));
    }

    // ========== ValidateHexInput ==========

    [Fact]
    public void ValidateHexInput_NullOrEmpty_IsValid()
    {
        Assert.True(HexHelper.ValidateHexInput(null).IsValid);
        Assert.True(HexHelper.ValidateHexInput("").IsValid);
    }

    [Fact]
    public void ValidateHexInput_PairedHexDigits_IsValid()
    {
        var r = HexHelper.ValidateHexInput("AA 55 03 01 19");
        Assert.True(r.IsValid);
        Assert.Equal(5, r.ByteCount);
        Assert.Equal(-1, r.InvalidIndex);
    }

    [Fact]
    public void ValidateHexInput_Lowercase_IsValid()
    {
        var r = HexHelper.ValidateHexInput("aabbcc");
        Assert.True(r.IsValid);
        Assert.Equal(3, r.ByteCount);
    }

    [Fact]
    public void ValidateHexInput_OddDigitCount_Invalid()
    {
        // Three hex digits — cannot form whole bytes. The marker index is end-of-string.
        var r = HexHelper.ValidateHexInput("AAB");
        Assert.False(r.IsValid);
        Assert.Equal(3, r.InvalidIndex);
        Assert.Equal(1, r.ByteCount);    // floor(3/2) = 1 byte attempted
    }

    [Fact]
    public void ValidateHexInput_InvalidCharacter_ReportsIndex()
    {
        // "AA 55Z CC" -> indices: 0=A 1=A 2=' ' 3=5 4=5 5=Z (the bad one) ...
        var r = HexHelper.ValidateHexInput("AA 55Z CC");
        Assert.False(r.IsValid);
        Assert.Equal(5, r.InvalidIndex);
    }

    [Fact]
    public void ValidateHexInput_NewlineAndTab_AreTreatedAsWhitespace()
    {
        var r = HexHelper.ValidateHexInput("AA\n55\t03");
        Assert.True(r.IsValid);
        Assert.Equal(3, r.ByteCount);
    }
}
