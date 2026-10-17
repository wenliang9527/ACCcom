namespace ACCcom.Core.Services;

/// <summary>Dictionary key for pending Modbus master transactions (slave id +
/// function code). A readonly record struct instead of the old interpolated
/// "a:b" string: the RX hot path used to allocate one key string per received
/// packet — on every serial event, even when no conversation was pending.</summary>
internal readonly record struct ModbusKey(byte SlaveId, byte FunctionCode);
