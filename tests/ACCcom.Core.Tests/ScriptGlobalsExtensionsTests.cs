using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class ScriptGlobalsExtensionsTests
{
    private ScriptGlobals CreateGlobals(byte[] data) => new() { RawData = data, Timestamp = DateTime.Now };

    [Fact]
    public void ToUInt32_LittleEndian()
    {
        var g = CreateGlobals(new byte[] { 0x78, 0x56, 0x34, 0x12 });
        Assert.Equal(0x12345678u, g.ToUInt32(0, false));
    }

    [Fact]
    public void ToUInt32_BigEndian()
    {
        var g = CreateGlobals(new byte[] { 0x12, 0x34, 0x56, 0x78 });
        Assert.Equal(0x12345678u, g.ToUInt32(0, true));
    }

    [Fact]
    public void ToInt32_Negative()
    {
        var g = CreateGlobals(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
        Assert.Equal(-1, g.ToInt32(0, false));
    }

    [Fact]
    public void ToDouble_LittleEndian()
    {
        // 1.0 in IEEE 754 double LE
        var bytes = BitConverter.GetBytes(1.0);
        var g = CreateGlobals(bytes);
        Assert.Equal(1.0, g.ToDouble(0, false));
    }

    [Fact]
    public void ToDouble_BigEndian_DoesNotMutate()
    {
        var bytes = BitConverter.GetBytes(3.14);
        var original = (byte[])bytes.Clone();
        var g = CreateGlobals(bytes);
        var val1 = g.ToDouble(0, false);
        var val2 = g.ToDouble(0, false);
        Assert.Equal(val1, val2);
    }

    [Fact]
    public void FromBcd_TwoBytes()
    {
        // BCD 0x23, 0x45 = 2345
        var g = CreateGlobals(new byte[] { 0x23, 0x45 });
        Assert.Equal(2345, g.FromBcd(0, 2));
    }

    [Fact]
    public void FromBcd_SingleByte()
    {
        var g = CreateGlobals(new byte[] { 0x99 });
        Assert.Equal(99, g.FromBcd(0, 1));
    }

    [Fact]
    public void ToFloat_BigEndian()
    {
        // 1.0f BE = 0x3F800000
        var g = CreateGlobals(new byte[] { 0x3F, 0x80, 0x00, 0x00 });
        Assert.Equal(1.0f, g.ToFloat(0, true));
    }

    [Fact]
    public void ToUInt32_OutOfBounds_ReturnsZero()
    {
        var g = CreateGlobals(new byte[] { 0x01, 0x02 });
        Assert.Equal(0u, g.ToUInt32(0));
    }

    [Fact]
    public void FromBcd_OutOfBounds_ReturnsZero()
    {
        var g = CreateGlobals(new byte[] { 0x01 });
        Assert.Equal(0, g.FromBcd(0, 2));
    }
}
