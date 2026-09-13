using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

/// <summary>
/// Maps a live Modbus transaction into the display row shown in the log
/// window. Extracted from ModbusViewModel.OnTransaction so the timeout
/// placeholder and OK/ERR status formatting are unit-testable and shared
/// with any future consumer (HTTP polling dashboard, MCP tools, …).
/// </summary>
public static class ModbusTransactionMapper
{
    /// <summary>Builds the log item for a transaction. A null response hex is
    /// shown as "(timeout)"; a successful transaction as "OK"; a failed one as
    /// "ERR: {message}" (falling back to a generic message when none is set).</summary>
    public static TransactionLogItem ToLogItem(ModbusTransaction tx)
    {
        ArgumentNullException.ThrowIfNull(tx);

        return new TransactionLogItem
        {
            Timestamp = tx.Timestamp,
            FunctionCode = tx.FunctionCode,
            SlaveId = tx.SlaveId,
            RequestHex = tx.RequestHex,
            ResponseHex = tx.ResponseHex ?? "(timeout)",
            Status = tx.IsSuccess ? "OK" : $"ERR: {tx.ErrorMessage ?? "failed"}"
        };
    }
}