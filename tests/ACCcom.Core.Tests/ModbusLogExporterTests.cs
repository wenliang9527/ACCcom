using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class ModbusLogExporterTests
{
    private static readonly DateTime FixedTs = new(2026, 9, 6, 14, 30, 45);

    private static TransactionLogItem MakeItem(
        int slaveId = 1,
        string request = "01 03 00 00",
        string response = "01 03 02 12 34",
        string status = "OK",
        ModbusFunctionCode fc = ModbusFunctionCode.ReadHoldingRegisters)
    {
        return new TransactionLogItem
        {
            Timestamp = FixedTs,
            SlaveId = (byte)slaveId,
            FunctionCode = fc,
            RequestHex = request,
            ResponseHex = response,
            Status = status
        };
    }

    [Fact]
    public void ExportCsv_header_and_row()
    {
        var csv = ModbusLogExporter.ExportCsv(new List<TransactionLogItem> { MakeItem() });

        var lines = csv.TrimEnd('\r', '\n').Split("\r\n");
        Assert.Equal("Timestamp,SlaveId,FunctionCode,RequestHex,ResponseHex,Status", lines[0]);
        Assert.Equal("2026-09-06 14:30:45,1,ReadHoldingRegisters,01 03 00 00,01 03 02 12 34,OK", lines[1]);
    }

    [Fact]
    public void ExportCsv_empty_list_only_header()
    {
        var csv = ModbusLogExporter.ExportCsv(new List<TransactionLogItem>());

        Assert.Single(csv.TrimEnd('\r', '\n').Split("\r\n"));
    }

    [Fact]
    public void EscapeCsv_plain_value_unchanged()
    {
        Assert.Equal("01 03 00 00", ModbusLogExporter.EscapeCsv("01 03 00 00"));
    }

    [Fact]
    public void EscapeCsv_null_or_empty_becomes_empty()
    {
        Assert.Equal("", ModbusLogExporter.EscapeCsv(null));
        Assert.Equal("", ModbusLogExporter.EscapeCsv(""));
    }

    [Fact]
    public void EscapeCsv_value_with_comma_quoted()
    {
        Assert.Equal("\"a,b\"", ModbusLogExporter.EscapeCsv("a,b"));
    }

    [Fact]
    public void EscapeCsv_value_with_quote_doubles_them()
    {
        Assert.Equal("\"say \"\"hi\"\"\"", ModbusLogExporter.EscapeCsv("say \"hi\""));
    }

    [Fact]
    public void EscapeCsv_value_with_newline_quoted()
    {
        Assert.Equal("\"line1\nline2\"", ModbusLogExporter.EscapeCsv("line1\nline2"));
    }

    [Fact]
    public void ExportCsv_quotes_special_fields()
    {
        var item = MakeItem(status: "ERR: bad,quote\"here");
        var csv = ModbusLogExporter.ExportCsv(new List<TransactionLogItem> { item });

        Assert.Contains("\"ERR: bad,quote\"\"here\"", csv);
    }

    [Fact]
    public void ExportJson_single_item_structure()
    {
        var json = ModbusLogExporter.ExportJson(new List<TransactionLogItem> { MakeItem() });

        Assert.Contains("\"timestamp\": \"2026-09-06 14:30:45\"", json);
        Assert.Contains("\"slaveId\": 1", json);
        Assert.Contains("\"functionCode\": \"ReadHoldingRegisters\"", json);
        Assert.Contains("\"requestHex\": \"01 03 00 00\"", json);
        Assert.Contains("\"responseHex\": \"01 03 02 12 34\"", json);
        Assert.Contains("\"status\": \"OK\"", json);
        Assert.StartsWith("[", json);
        Assert.EndsWith("]" + Environment.NewLine, json);
    }

    [Fact]
    public void ExportJson_multiple_items_comma_separated()
    {
        var json = ModbusLogExporter.ExportJson(new List<TransactionLogItem>
        {
            MakeItem(slaveId: 1),
            MakeItem(slaveId: 2)
        });

        // First object closes with "}," and the last with "}".
        Assert.Contains("  },", json);
        Assert.Contains("  }" + Environment.NewLine + "]", json);
    }

    [Fact]
    public void ExportJson_empty_list_is_brackets()
    {
        var json = ModbusLogExporter.ExportJson(new List<TransactionLogItem>());

        Assert.Equal("[" + Environment.NewLine + "]" + Environment.NewLine, json);
    }

    [Fact]
    public void ExportTxt_contains_header_and_fields()
    {
        var txt = ModbusLogExporter.ExportTxt(new List<TransactionLogItem> { MakeItem() });

        Assert.Contains("=== MODBUS Transaction Log ===", txt);
        Assert.Contains("Total Records: 1", txt);
        Assert.Contains("Time:     2026-09-06 14:30:45", txt);
        Assert.Contains("Slave ID: 1", txt);
        Assert.Contains("Function: ReadHoldingRegisters", txt);
        Assert.Contains("Request:  01 03 00 00", txt);
        Assert.Contains("Response: 01 03 02 12 34", txt);
        Assert.Contains("Status:   OK", txt);
    }

    [Fact]
    public void ExportTxt_null_response_shows_timeout()
    {
        var item = MakeItem();
        item.ResponseHex = null!;

        var txt = ModbusLogExporter.ExportTxt(new List<TransactionLogItem> { item });

        Assert.Contains("Response: (timeout)", txt);
    }

    [Fact]
    public void ExportTxt_separator_lines()
    {
        var txt = ModbusLogExporter.ExportTxt(new List<TransactionLogItem> { MakeItem() });

        Assert.Contains(new string('=', 80), txt);
        Assert.Contains(new string('-', 60), txt);
    }
}