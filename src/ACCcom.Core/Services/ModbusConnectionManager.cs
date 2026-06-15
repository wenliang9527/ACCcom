using System.Collections.Concurrent;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class ModbusConnectionManager : IDisposable
{
    private readonly ConcurrentDictionary<string, ManagedConnection> _connections = new();
    private bool _disposed;

    public string DefaultConnectionId => "default";

    public ModbusService GetDefaultService(ISerialService serial)
    {
        var managed = _connections.GetOrAdd(DefaultConnectionId, _ =>
        {
            var transport = new ModbusRtuTransport(serial);
            var svc = new ModbusService(transport);
            return new ManagedConnection(transport, svc, "RTU");
        });
        return managed.Service;
    }

    public ModbusService CreateTcpConnection(string connectionId, string host, int port)
    {
        if (_connections.ContainsKey(connectionId))
            throw new InvalidOperationException($"Connection '{connectionId}' already exists");

        var transport = new ModbusTcpTransport(host, port);
        var svc = new ModbusService(transport);
        _connections[connectionId] = new ManagedConnection(transport, svc, $"TCP:{host}:{port}");
        return svc;
    }

    public ModbusService? GetService(string connectionId)
    {
        return _connections.TryGetValue(connectionId, out var managed) ? managed.Service : null;
    }

    public bool RemoveConnection(string connectionId)
    {
        if (connectionId == DefaultConnectionId) return false;
        if (_connections.TryRemove(connectionId, out var managed))
        {
            managed.Transport.Dispose();
            return true;
        }
        return false;
    }

    public IReadOnlyDictionary<string, string> GetActiveConnections()
    {
        return _connections.ToDictionary(kv => kv.Key, kv => kv.Value.Description);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var kv in _connections)
        {
            kv.Value.Transport.Dispose();
        }
        _connections.Clear();
    }

    private record ManagedConnection(IModbusTransport Transport, ModbusService Service, string Description);
}
