using System.Text;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

/// <summary>
/// Formats a Modbus transaction log for file export. CSV gets RFC-style
/// quoting (fields containing comma/quote/newline are wrapped in quotes with
/// doubled quotes); JSON is hand-built (stable field order, no dependency on
/// serializer config); TXT is a human-readable aligned block. Extracted from
/// ModbusViewModel so the exact formats are locked by tests.
/// </summary>
public static class ModbusLogExporter
{
    public static string ExportCsv(List<TransactionLogItem> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,SlaveId,FunctionCode,RequestHex,ResponseHex,Status");
        foreach (var item in items)
        {
            sb.AppendLine($"{item.Timestamp:yyyy-MM-dd HH:mm:ss},{item.SlaveId},{item.FunctionCode},{EscapeCsv(item.RequestHex)},{EscapeCsv(item.ResponseHex)},{EscapeCsv(item.Status)}");
        }
        return sb.ToString();
    }

    /// <summary>Quotes a CSV field when it contains a comma, double quote or
    /// newline, doubling embedded quotes; null/empty becomes "".</summary>
    public static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    public static string ExportJson(List<TransactionLogItem> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[");
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            sb.AppendLine("  {");
            sb.AppendLine($"    \"timestamp\": \"{item.Timestamp:yyyy-MM-dd HH:mm:ss}\",");
            sb.AppendLine($"    \"slaveId\": {item.SlaveId},");
            sb.AppendLine($"    \"functionCode\": \"{item.FunctionCode}\",");
            sb.AppendLine($"    \"requestHex\": \"{item.RequestHex}\",");
            sb.AppendLine($"    \"responseHex\": \"{item.ResponseHex ?? ""}\",");
            sb.AppendLine($"    \"status\": \"{item.Status}\"");
            sb.Append(i < items.Count - 1 ? "  }," : "  }");
            sb.AppendLine();
        }
        sb.AppendLine("]");
        return sb.ToString();
    }

    public static string ExportTxt(List<TransactionLogItem> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== MODBUS Transaction Log ===");
        sb.AppendLine($"Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Total Records: {items.Count}");
        sb.AppendLine(new string('=', 80));
        sb.AppendLine();
        foreach (var item in items)
        {
            sb.AppendLine($"Time:     {item.Timestamp:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Slave ID: {item.SlaveId}");
            sb.AppendLine($"Function: {item.FunctionCode}");
            sb.AppendLine($"Request:  {item.RequestHex}");
            sb.AppendLine($"Response: {item.ResponseHex ?? "(timeout)"}");
            sb.AppendLine($"Status:   {item.Status}");
            sb.AppendLine(new string('-', 60));
        }
        return sb.ToString();
    }
}
