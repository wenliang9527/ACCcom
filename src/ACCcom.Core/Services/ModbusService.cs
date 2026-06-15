using ACCcom.Core.Models;
using System.Threading;

namespace ACCcom.Core.Services;

public class ModbusService : IDisposable
{
    private readonly IModbusTransport _transport;
    private bool _disposed;

    public event Action<ModbusTransaction>? OnTransaction;

    public ModbusService(ISerialService serial)
        : this(new ModbusRtuTransport(serial)) { }

    public ModbusService(IModbusTransport transport)
    {
        _transport = transport;
    }

    public Task<ModbusResponse> ReadCoilsAsync(byte slaveId, ushort startAddr, ushort count, int timeoutMs = 1000, CancellationToken ct = default)
        => ExecuteAsync(slaveId, ModbusFunctionCode.ReadCoils, BuildReadRequest(startAddr, count), timeoutMs, ct);

    public Task<ModbusResponse> ReadDiscreteInputsAsync(byte slaveId, ushort startAddr, ushort count, int timeoutMs = 1000, CancellationToken ct = default)
        => ExecuteAsync(slaveId, ModbusFunctionCode.ReadDiscreteInputs, BuildReadRequest(startAddr, count), timeoutMs, ct);

    public Task<ModbusResponse> ReadHoldingRegistersAsync(byte slaveId, ushort startAddr, ushort count, int timeoutMs = 1000, CancellationToken ct = default)
        => ExecuteAsync(slaveId, ModbusFunctionCode.ReadHoldingRegisters, BuildReadRequest(startAddr, count), timeoutMs, ct);

    public Task<ModbusResponse> ReadInputRegistersAsync(byte slaveId, ushort startAddr, ushort count, int timeoutMs = 1000, CancellationToken ct = default)
        => ExecuteAsync(slaveId, ModbusFunctionCode.ReadInputRegisters, BuildReadRequest(startAddr, count), timeoutMs, ct);

    public Task<ModbusResponse> WriteSingleCoilAsync(byte slaveId, ushort addr, bool value, int timeoutMs = 1000, CancellationToken ct = default)
        => ExecuteAsync(slaveId, ModbusFunctionCode.WriteSingleCoil, BuildWriteCoilRequest(addr, value), timeoutMs, ct);

    public Task<ModbusResponse> WriteSingleRegisterAsync(byte slaveId, ushort addr, ushort value, int timeoutMs = 1000, CancellationToken ct = default)
        => ExecuteAsync(slaveId, ModbusFunctionCode.WriteSingleRegister, BuildWriteRegisterRequest(addr, value), timeoutMs, ct);

    public Task<ModbusResponse> WriteMultipleCoilsAsync(byte slaveId, ushort startAddr, bool[] values, int timeoutMs = 1000, CancellationToken ct = default)
        => ExecuteAsync(slaveId, ModbusFunctionCode.WriteMultipleCoils, BuildWriteCoilsRequest(startAddr, values), timeoutMs, ct);

    public Task<ModbusResponse> WriteMultipleRegistersAsync(byte slaveId, ushort startAddr, ushort[] values, int timeoutMs = 1000, CancellationToken ct = default)
        => ExecuteAsync(slaveId, ModbusFunctionCode.WriteMultipleRegisters, BuildWriteRegistersRequest(startAddr, values), timeoutMs, ct);

    public Task<ModbusResponse> MaskWriteRegisterAsync(byte slaveId, ushort addr, ushort andMask, ushort orMask, int timeoutMs = 1000, CancellationToken ct = default)
        => ExecuteAsync(slaveId, ModbusFunctionCode.MaskWriteRegister, BuildMaskWriteRequest(addr, andMask, orMask), timeoutMs, ct);

    public Task<ModbusResponse> ReadWriteMultipleRegistersAsync(byte slaveId, ushort readAddr, ushort readCount, ushort writeAddr, ushort[] writeValues, int timeoutMs = 1000, CancellationToken ct = default)
        => ExecuteAsync(slaveId, ModbusFunctionCode.ReadWriteMultipleRegisters, BuildReadWriteRegistersRequest(readAddr, readCount, writeAddr, writeValues), timeoutMs, ct);

    private async Task<ModbusResponse> ExecuteAsync(byte slaveId, ModbusFunctionCode function, byte[] pdu, int timeoutMs, CancellationToken ct = default)
    {
        var reqHex = "";
        try
        {
            ct.ThrowIfCancellationRequested();
            var responseBytes = await _transport.SendReceiveAsync(slaveId, (byte)function, pdu, timeoutMs, ct).ConfigureAwait(false);
            reqHex = BytesToHex(BuildAdu(slaveId, function, pdu));

            var respFuncByte = responseBytes[1];
            var isError = (respFuncByte & 0x80) != 0;

            byte? exceptionCode = null;
            byte[] data;
            if (isError)
            {
                exceptionCode = responseBytes.Length > 2 ? responseBytes[2] : null;
                data = [];
            }
            else
            {
                var dataLength = responseBytes.Length - 2;
                data = new byte[dataLength];
                if (dataLength > 0)
                    Array.Copy(responseBytes, 2, data, 0, dataLength);
            }

            var result = new ModbusResponse
            {
                IsError = isError,
                SlaveId = slaveId,
                FunctionCode = function,
                ExceptionCode = exceptionCode,
                Data = data,
                RawResponseHex = BytesToHex(responseBytes)
            };

            NotifyTransaction(slaveId, function, reqHex, result);
            return result;
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or InvalidOperationException)
        {
            var result = new ModbusResponse { IsError = true, SlaveId = slaveId, FunctionCode = function, ErrorMessage = ex.Message };
            NotifyTransaction(slaveId, function, reqHex, result);
            return result;
        }
        catch (Exception ex)
        {
            return new ModbusResponse { IsError = true, SlaveId = slaveId, FunctionCode = function, ErrorMessage = ex.Message };
        }
    }

    private void NotifyTransaction(byte slaveId, ModbusFunctionCode function, string reqHex, ModbusResponse resp)
    {
        OnTransaction?.Invoke(new ModbusTransaction
        {
            Timestamp = DateTime.Now,
            SlaveId = slaveId,
            FunctionCode = function,
            RequestHex = reqHex,
            ResponseHex = resp.RawResponseHex,
            IsSuccess = !resp.IsError,
            ErrorMessage = resp.ErrorMessage,
            Response = resp
        });
    }

    private static byte[] BuildAdu(byte slaveId, ModbusFunctionCode function, byte[] pdu)
    {
        var adu = new byte[1 + pdu.Length + 2];
        adu[0] = slaveId;
        adu[1] = (byte)function;
        Array.Copy(pdu, 0, adu, 2, pdu.Length);
        var crc = CrcHelper.Crc16(adu.AsSpan(0, adu.Length - 2));
        adu[^2] = (byte)(crc & 0xFF);
        adu[^1] = (byte)((crc >> 8) & 0xFF);
        return adu;
    }

    private static string BytesToHex(byte[] data)
        => HexHelper.BytesToHexSpaced(data, 0, data.Length);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _transport.Dispose();
    }

    internal static byte[] BuildReadRequest(ushort startAddr, ushort count) => ModbusRtuTransport.BuildReadRequest(startAddr, count);
    internal static byte[] BuildMaskWriteRequest(ushort addr, ushort andMask, ushort orMask) => ModbusRtuTransport.BuildMaskWriteRequest(addr, andMask, orMask);
    internal static byte[] BuildReadWriteRegistersRequest(ushort readAddr, ushort readCount, ushort writeAddr, ushort[] writeValues) => ModbusRtuTransport.BuildReadWriteRegistersRequest(readAddr, readCount, writeAddr, writeValues);
    internal static byte[] BuildWriteCoilRequest(ushort addr, bool value) => ModbusRtuTransport.BuildWriteCoilRequest(addr, value);
    internal static byte[] BuildWriteRegisterRequest(ushort addr, ushort value) => ModbusRtuTransport.BuildWriteRegisterRequest(addr, value);
    internal static byte[] BuildWriteCoilsRequest(ushort startAddr, bool[] values) => ModbusRtuTransport.BuildWriteCoilsRequest(startAddr, values);
    internal static byte[] BuildWriteRegistersRequest(ushort startAddr, ushort[] values) => ModbusRtuTransport.BuildWriteRegistersRequest(startAddr, values);
    internal static ushort Crc16(ReadOnlySpan<byte> data) => CrcHelper.Crc16(data);
    internal static byte[] HexStringToBytes(string hex) => ModbusRtuTransport.HexStringToBytes(hex);
}
