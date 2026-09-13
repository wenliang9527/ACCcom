using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class ModbusTransactionMapperTests
{
    private static readonly DateTime FixedTs = new(2026, 9, 13, 10, 0, 0);

    private static ModbusTransaction MakeTx(
        bool isSuccess = true,
        string? responseHex = "01 03 02 12 34",
        string? errorMessage = null)
        => new()
        {
            Timestamp = FixedTs,
            FunctionCode = ModbusFunctionCode.ReadHoldingRegisters,
            SlaveId = 5,
            RequestHex = "01 03 00 00 00 01",
            ResponseHex = responseHex,
            IsSuccess = isSuccess,
            ErrorMessage = errorMessage
        };

    [Fact]
    public void ToLogItem_maps_all_fields()
    {
        var item = ModbusTransactionMapper.ToLogItem(MakeTx());

        Assert.Equal(FixedTs, item.Timestamp);
        Assert.Equal(ModbusFunctionCode.ReadHoldingRegisters, item.FunctionCode);
        Assert.Equal((byte)5, item.SlaveId);
        Assert.Equal("01 03 00 00 00 01", item.RequestHex);
        Assert.Equal("01 03 02 12 34", item.ResponseHex);
        Assert.Equal("OK", item.Status);
    }

    [Fact]
    public void ToLogItem_null_response_hex_shows_timeout_placeholder()
    {
        var item = ModbusTransactionMapper.ToLogItem(MakeTx(responseHex: null));

        Assert.Equal("(timeout)", item.ResponseHex);
        Assert.Equal("OK", item.Status);
    }

    [Fact]
    public void ToLogItem_failed_transaction_shows_error_status()
    {
        var item = ModbusTransactionMapper.ToLogItem(MakeTx(isSuccess: false, errorMessage: "bad CRC"));

        Assert.Equal("ERR: bad CRC", item.Status);
    }

    [Fact]
    public void ToLogItem_failed_transaction_without_message_uses_fallback()
    {
        var item = ModbusTransactionMapper.ToLogItem(MakeTx(isSuccess: false, errorMessage: null));

        Assert.Equal("ERR: failed", item.Status);
    }

    [Fact]
    public void ToLogItem_null_transaction_throws()
    {
        Assert.Throws<ArgumentNullException>(() => ModbusTransactionMapper.ToLogItem(null!));
    }
}