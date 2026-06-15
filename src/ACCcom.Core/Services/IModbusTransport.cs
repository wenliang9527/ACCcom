namespace ACCcom.Core.Services;

public interface IModbusTransport : IDisposable
{
    Task<byte[]> SendReceiveAsync(byte slaveId, byte functionCode, byte[] pdu, int timeoutMs, CancellationToken ct = default);
}
