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
}
