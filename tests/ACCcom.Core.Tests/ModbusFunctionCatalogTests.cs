using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class ModbusFunctionCatalogTests
{
    [Fact]
    public void FromIndex_maps_all_dropdown_indices()
    {
        Assert.Equal(ModbusFunctionCode.ReadCoils, ModbusFunctionCatalog.FromIndex(0));
        Assert.Equal(ModbusFunctionCode.ReadDiscreteInputs, ModbusFunctionCatalog.FromIndex(1));
        Assert.Equal(ModbusFunctionCode.ReadHoldingRegisters, ModbusFunctionCatalog.FromIndex(2));
        Assert.Equal(ModbusFunctionCode.ReadInputRegisters, ModbusFunctionCatalog.FromIndex(3));
        Assert.Equal(ModbusFunctionCode.WriteSingleCoil, ModbusFunctionCatalog.FromIndex(4));
        Assert.Equal(ModbusFunctionCode.WriteSingleRegister, ModbusFunctionCatalog.FromIndex(5));
        Assert.Equal(ModbusFunctionCode.WriteMultipleCoils, ModbusFunctionCatalog.FromIndex(6));
        Assert.Equal(ModbusFunctionCode.WriteMultipleRegisters, ModbusFunctionCatalog.FromIndex(7));
        Assert.Equal(ModbusFunctionCode.MaskWriteRegister, ModbusFunctionCatalog.FromIndex(8));
        Assert.Equal(ModbusFunctionCode.ReadWriteMultipleRegisters, ModbusFunctionCatalog.FromIndex(9));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    [InlineData(999)]
    public void FromIndex_out_of_range_falls_back_to_read_holding(int index)
    {
        // Matches the original switch's default case.
        Assert.Equal(ModbusFunctionCode.ReadHoldingRegisters, ModbusFunctionCatalog.FromIndex(index));
    }

    [Fact]
    public void ToIndex_roundtrips_from_index()
    {
        for (int i = 0; i < 10; i++)
            Assert.Equal(i, ModbusFunctionCatalog.ToIndex(ModbusFunctionCatalog.FromIndex(i)));
    }

    [Fact]
    public void ToIndex_unknown_code_returns_minus_one()
    {
        // 0x08 is not in the dropdown catalog.
        Assert.Equal(-1, ModbusFunctionCatalog.ToIndex((ModbusFunctionCode)0x08));
    }

    [Fact]
    public void DisplayNames_matches_original_order()
    {
        Assert.Equal(
        [
            "01 Read Coils",
            "02 Read Discrete Inputs",
            "03 Read Holding Registers",
            "04 Read Input Registers",
            "05 Write Single Coil",
            "06 Write Single Register",
            "15 Write Multiple Coils",
            "16 Write Multiple Registers",
            "22 Mask Write Register",
            "23 Read/Write Multiple Registers"
        ], ModbusFunctionCatalog.DisplayNames);
    }

    [Fact]
    public void DisplayNames_count_matches_from_index_range()
    {
        // The dropdown list and the index mapping must stay in lockstep:
        // FromIndex is valid for exactly DisplayNames.Count entries.
        var names = ModbusFunctionCatalog.DisplayNames;
        Assert.Equal(10, names.Count);
        Assert.Equal(ModbusFunctionCode.ReadWriteMultipleRegisters, ModbusFunctionCatalog.FromIndex(names.Count - 1));
    }

    [Fact]
    public void GetDisplayName_known_code()
    {
        Assert.Equal("03 Read Holding Registers", ModbusFunctionCatalog.GetDisplayName(ModbusFunctionCode.ReadHoldingRegisters));
        Assert.Equal("15 Write Multiple Coils", ModbusFunctionCatalog.GetDisplayName(ModbusFunctionCode.WriteMultipleCoils));
    }

    [Fact]
    public void GetDisplayName_unknown_code_returns_raw_value()
    {
        Assert.Equal("8", ModbusFunctionCatalog.GetDisplayName((ModbusFunctionCode)0x08));
    }

    [Fact]
    public void Protocol_codes_match_modbus_spec()
    {
        // Guard against someone reordering the enum or the catalog.
        Assert.Equal(0x01, (byte)ModbusFunctionCatalog.FromIndex(0));
        Assert.Equal(0x0F, (byte)ModbusFunctionCatalog.FromIndex(6));
        Assert.Equal(0x10, (byte)ModbusFunctionCatalog.FromIndex(7));
        Assert.Equal(22, (byte)ModbusFunctionCatalog.FromIndex(8));
        Assert.Equal(23, (byte)ModbusFunctionCatalog.FromIndex(9));
    }
}