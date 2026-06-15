using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

/// <summary>
/// Modbus 设备扫描器，用于自动发现网络上的 Modbus 从站设备
/// </summary>
public class ModbusScanner : IDisposable
{
    private readonly ModbusService _modbus;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public event Action<ModbusScanResult>? OnDeviceFound;
    public event Action<int>? OnScanProgress;
    public event Action? OnScanCompleted;

    public ModbusScanner(ModbusService modbus)
    {
        _modbus = modbus;
    }

    /// <summary>
    /// 扫描指定范围的从站地址
    /// </summary>
    public async Task<List<ModbusScanResult>> ScanAsync(
        byte startAddress = 1,
        byte endAddress = 247,
        int timeoutMs = 500,
        CancellationToken ct = default)
    {
        var results = new List<ModbusScanResult>();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            for (byte addr = startAddress; addr <= endAddress; addr++)
            {
                _cts.Token.ThrowIfCancellationRequested();

                var result = await ProbeDeviceAsync(addr, timeoutMs, _cts.Token).ConfigureAwait(false);
                if (result != null)
                {
                    results.Add(result);
                    OnDeviceFound?.Invoke(result);
                }

                OnScanProgress?.Invoke(addr - startAddress + 1);
            }
        }
        finally
        {
            OnScanCompleted?.Invoke();
        }

        return results;
    }

    /// <summary>
    /// 探测单个从站地址
    /// </summary>
    private async Task<ModbusScanResult?> ProbeDeviceAsync(byte slaveId, int timeoutMs, CancellationToken ct)
    {
        try
        {
            var result = await _modbus.ReadHoldingRegistersAsync(
                slaveId, 0, 1, timeoutMs, ct).ConfigureAwait(false);

            if (!result.IsError)
            {
                return new ModbusScanResult
                {
                    SlaveId = slaveId,
                    IsOnline = true,
                    ResponseTimeMs = timeoutMs,
                    FirstRegisterValue = result.Data.Length >= 2
                        ? (ushort)((result.Data[0] << 8) | result.Data[1])
                        : (ushort)0
                };
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 停止扫描
    /// </summary>
    public void StopScan()
    {
        _cts?.Cancel();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Dispose();
    }
}

/// <summary>
/// Modbus 扫描结果
/// </summary>
public class ModbusScanResult
{
    public byte SlaveId { get; set; }
    public bool IsOnline { get; set; }
    public int ResponseTimeMs { get; set; }
    public ushort FirstRegisterValue { get; set; }

    public override string ToString()
        => $"Slave 0x{SlaveId:X2} (FirstReg=0x{FirstRegisterValue:X4}, RTT={ResponseTimeMs}ms)";
}
