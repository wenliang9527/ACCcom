using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class MultiPortService : IDisposable
{
    private readonly Dictionary<string, PortInstance> _ports = new();
    private readonly object _lock = new();

    public event Action<LogEntry>? OnDataReceived;
    public event Action<string, string>? OnPortError;
    public event Action<string>? OnPortDisconnected;

    public IReadOnlyDictionary<string, PortInstance> Ports => _ports;

    public bool OpenPort(string tag, SerialConfig config)
    {
        lock (_lock)
        {
            if (_ports.ContainsKey(tag)) return _ports[tag].Service.IsOpen;

            var service = new SerialService();
            service.OnDataReceived += entry =>
            {
                entry.PortTag = tag;
                OnDataReceived?.Invoke(entry);
            };
            service.OnError += msg => OnPortError?.Invoke(tag, msg);
            service.OnDisconnected += () => OnPortDisconnected?.Invoke(tag);

            if (!service.Open(config))
            {
                service.Dispose();
                return false;
            }

            _ports[tag] = new PortInstance { Tag = tag, Service = service, Config = config };
            return true;
        }
    }

    public bool ClosePort(string tag)
    {
        lock (_lock)
        {
            if (!_ports.TryGetValue(tag, out var instance)) return true;
            var result = instance.Service.Close();
            instance.Service.Dispose();
            _ports.Remove(tag);
            return result;
        }
    }

    public bool SendToPort(string tag, string data, bool isHex = false)
    {
        lock (_lock)
        {
            if (!_ports.TryGetValue(tag, out var instance)) return false;
            return instance.Service.Send(data, isHex);
        }
    }

    public void CloseAll()
    {
        lock (_lock)
        {
            foreach (var instance in _ports.Values)
            {
                instance.Service.Close();
                instance.Service.Dispose();
            }
            _ports.Clear();
        }
    }

    public void Dispose() => CloseAll();
}

public class PortInstance
{
    public string Tag { get; set; } = "";
    public SerialService Service { get; set; } = null!;
    public SerialConfig Config { get; set; } = null!;
}
