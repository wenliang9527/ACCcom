using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public record SlaveInfo(string Id, byte SlaveId, string TransportType, string ConnectionParam, bool IsRunning);

public enum RegisterType { Coil, DiscreteInput, HoldingRegister, InputRegister }

public class ModbusSlaveService : IDisposable
{
    private readonly Dictionary<string, (ModbusSlaveDevice Device, IDisposable Transport)> _slaves = new();
    private readonly object _lock = new();
    private bool _disposed;
    private int _nextId;

    public event Action<ModbusTransaction>? OnTransaction;

    public string CreateSlave(byte slaveId, string transportType, string connectionParam,
        int coils = 1024, int discreteInputs = 1024, int holdingRegisters = 256, int inputRegisters = 256)
    {
        var device = new ModbusSlaveDevice(slaveId, coils, discreteInputs, holdingRegisters, inputRegisters);
        device.OnTransaction += tx => OnTransaction?.Invoke(tx);
        IDisposable transport = transportType.ToLowerInvariant() switch
        {
            "rtu" => CreateRtuTransport(connectionParam, device),
            "tcp" => CreateTcpTransport(connectionParam, device),
            _ => throw new ArgumentException($"Unsupported transport: {transportType}")
        };
        var id = $"slave_{++_nextId}";
        lock (_lock) _slaves[id] = (device, transport);
        return id;
    }

    private IDisposable CreateRtuTransport(string port, ModbusSlaveDevice device)
    {
        var serial = new SerialService();
        serial.Open(new SerialConfig { PortName = port, BaudRate = 115200 });
        var transport = new ModbusRtuSlaveTransport(serial);
        transport.OnRequestReceived = (id, pdu) => id != device.SlaveId ? [] : device.HandleRequest(pdu[0], pdu[1..]);
        transport.Start();
        return new TransportGroup(transport, serial);
    }

    private IDisposable CreateTcpTransport(string port, ModbusSlaveDevice device)
    {
        if (!int.TryParse(port, out var portNum) || portNum <= 0 || portNum > 65535)
            throw new ArgumentException($"Invalid TCP port: {port}");

        var transport = new ModbusTcpSlaveTransport(portNum);
        transport.OnRequestReceived = (id, pdu) => id != device.SlaveId ? [] : device.HandleRequest(pdu[0], pdu[1..]);
        transport.Start();
        return transport;
    }

    public void RemoveSlave(string slaveId)
    {
        lock (_lock) { if (_slaves.TryGetValue(slaveId, out var e)) { e.Transport.Dispose(); _slaves.Remove(slaveId); } }
    }

    public IEnumerable<SlaveInfo> GetActiveSlaves()
    {
        lock (_lock) { return _slaves.Select(kv => new SlaveInfo(kv.Key, kv.Value.Device.SlaveId, kv.Value.Transport is TransportGroup ? "rtu" : "tcp", "active", true)).ToList(); }
    }

    public ModbusSlaveDevice? GetDevice(string slaveId)
    {
        lock (_lock) { return _slaves.TryGetValue(slaveId, out var e) ? e.Device : null; }
    }

    public void WriteRegister(string slaveId, RegisterType type, ushort addr, ushort value)
    {
        var d = GetDevice(slaveId); if (d == null) return;
        switch (type) { case RegisterType.Coil: d.SetCoil(addr, value != 0); break; case RegisterType.HoldingRegister: d.SetHoldingRegister(addr, value); break; case RegisterType.DiscreteInput: d.SetDiscreteInput(addr, value != 0); break; case RegisterType.InputRegister: d.SetInputRegister(addr, value); break; }
    }

    public ushort ReadRegister(string slaveId, RegisterType type, ushort addr)
    {
        var d = GetDevice(slaveId); if (d == null) return 0;
        return type switch { RegisterType.Coil => (ushort)(d.GetCoil(addr) ? 1 : 0), RegisterType.HoldingRegister => d.GetHoldingRegister(addr), RegisterType.DiscreteInput => (ushort)(d.GetDiscreteInput(addr) ? 1 : 0), RegisterType.InputRegister => d.GetInputRegister(addr), _ => 0 };
    }

    private record TransportGroup(IDisposable Transport, SerialService Serial) : IDisposable
    { public void Dispose() { Transport.Dispose(); Serial.Dispose(); } }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        lock (_lock) { foreach (var e in _slaves.Values) e.Transport.Dispose(); _slaves.Clear(); }
    }
}
