using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class MultiPortService : IDisposable
{
    private readonly Dictionary<string, PortInstance> _ports = new();
    private readonly object _lock = new();
    private readonly Func<ISerialService> _serviceFactory;

    public event Action<LogEntry>? OnDataReceived;
    public event Action<string, string>? OnPortError;
    public event Action<string>? OnPortDisconnected;

    /// <summary>Snapshot of currently open ports. Returns a copy so callers can
    /// enumerate without holding the lock while another thread closes a port
    /// (a bare dictionary reference would throw on concurrent mutation).</summary>
    public IReadOnlyDictionary<string, PortInstance> Ports
    {
        get
        {
            lock (_lock) { return new Dictionary<string, PortInstance>(_ports); }
        }
    }

    /// <summary>Thread-safe lookup of a single open port; returns null when the
    /// tag is not open.</summary>
    public PortInstance? GetPort(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return null;
        lock (_lock)
        {
            return _ports.TryGetValue(tag, out var inst) ? inst : null;
        }
    }

    public MultiPortService() : this(() => new SerialService())
    {
    }

    /// <summary>
    /// Test seam: lets callers supply their own serial service factory (e.g.
    /// VirtualSerialService) so multi-port data routing is testable without
    /// real hardware.
    /// </summary>
    public MultiPortService(Func<ISerialService> serviceFactory)
    {
        ArgumentNullException.ThrowIfNull(serviceFactory);
        _serviceFactory = serviceFactory;
    }

    public bool OpenPort(string? tag, SerialConfig? config)
    {
        // A null tag would throw ArgumentNullException from ContainsKey; a null
        // config fails cleanly through the service's own Open guard.
        if (string.IsNullOrEmpty(tag) || config == null) return false;

        // Fast path: already open. Kept in its own short lock so a slow
        // service.Open below cannot serialize unrelated lookups/opens.
        lock (_lock)
        {
            if (_ports.TryGetValue(tag, out var existing)) return existing.Service.IsOpen;
        }

        // The expensive part (real port open can take seconds) runs OUTSIDE
        // _lock ¡ª otherwise one slow open blocks GetPort/SendToPort/close for
        // every other port. A concurrent opener of the same tag is resolved by
        // the double-check below (loser tears down its own service).
        var service = CreateWiredService(tag);

        bool opened;
        try
        {
            opened = service.Open(config);
        }
        catch
        {
            // Old code leaked the service when Open threw (it propagated while
            // holding _lock). Preserve the exception but release resources.
            service.Dispose();
            throw;
        }

        if (!opened)
        {
            service.Dispose();
            return false;
        }

        return RegisterOpenPort(tag, config, service);
    }

    /// <summary>Async twin of <see cref="OpenPort"/>: identical wiring, fast
    /// path and double-check registration, but the open attempt itself goes
    /// through <see cref="ISerialService.OpenAsync"/> so retry sleeps do not
    /// pin a thread-pool thread.</summary>
    public async Task<bool> OpenPortAsync(string? tag, SerialConfig? config)
    {
        if (string.IsNullOrEmpty(tag) || config == null) return false;

        lock (_lock)
        {
            if (_ports.TryGetValue(tag, out var existing)) return existing.Service.IsOpen;
        }

        var service = CreateWiredService(tag);

        bool opened;
        try
        {
            opened = await service.OpenAsync(config).ConfigureAwait(false);
        }
        catch
        {
            service.Dispose();
            throw;
        }

        if (!opened)
        {
            service.Dispose();
            return false;
        }

        return RegisterOpenPort(tag, config, service);
    }

    /// <summary>Creates the per-tag service and wires its events into the
    /// multi-port routing planes (RX entries get tagged, errors/disconnects
    /// are re-raised with the tag).</summary>
    private ISerialService CreateWiredService(string tag)
    {
        var service = _serviceFactory();
        service.OnDataReceived += entry =>
        {
            entry.PortTag = tag;
            OnDataReceived?.Invoke(entry);
        };
        service.OnError += msg => OnPortError?.Invoke(tag, msg);
        service.OnDisconnected += () => OnPortDisconnected?.Invoke(tag);
        return service;
    }

    /// <summary>Registers a successfully opened service under the tag, or tears
    /// it down when a concurrent opener won the race (reports the winner's
    /// state, matching the fast-path result).</summary>
    private bool RegisterOpenPort(string tag, SerialConfig config, ISerialService service)
    {
        bool won;
        lock (_lock)
        {
            if (_ports.ContainsKey(tag))
            {
                won = false;
            }
            else
            {
                _ports[tag] = new PortInstance { Tag = tag, Service = service, Config = config };
                won = true;
            }
        }

        if (!won)
        {
            service.Close();
            service.Dispose();
            lock (_lock) { return _ports.TryGetValue(tag, out var winner) && winner.Service.IsOpen; }
        }
        return true;
    }

    public bool ClosePort(string tag)
    {
        // Null/empty tag falls through to the same "not open" no-op as any
        // unknown tag: Dictionary.TryGetValue(null) would otherwise throw
        // ArgumentNullException instead of returning the documented result.
        if (string.IsNullOrEmpty(tag)) return true;

        // Remove under the lock, then close/dispose OUTSIDE it: Close on real
        // hardware can take noticeable time and must not block GetPort/Send
        // for other tags. Removal is the mutual-exclusion point, so a
        // concurrent ClosePort/CloseAll never double-disposes the instance.
        PortInstance? instance;
        lock (_lock)
        {
            if (!_ports.TryGetValue(tag, out instance)) return true;
            _ports.Remove(tag);
        }

        var result = instance.Service.Close();
        instance.Service.Dispose();
        return result;
    }

    public bool SendToPort(string tag, string data, bool isHex = false)
    {
        // Matches the OpenPort/GetPort contract: a null/empty tag is treated as
        // "no such port" and fails cleanly instead of throwing from
        // Dictionary.TryGetValue(null).
        if (string.IsNullOrEmpty(tag)) return false;

        // Lookup under the lock, Send outside it: a slow/blocking write must
        // not hold _lock (that would stall GetPort and other ports' sends).
        PortInstance? instance;
        lock (_lock)
        {
            if (!_ports.TryGetValue(tag, out instance)) return false;
        }

        try
        {
            return instance.Service.Send(data, isHex);
        }
        catch (ObjectDisposedException)
        {
            // Narrowed window: the port was removed (and disposed) between the
            // lookup and the send â€?same observable result as a send to a port
            // that was never open.
            return false;
        }
    }

    public void CloseAll()
    {
        // Detach all instances under the lock, close them outside it (see
        // ClosePort): a slow Close on one port must not block new lookups.
        List<PortInstance> instances;
        lock (_lock)
        {
            if (_ports.Count == 0) return;
            instances = new List<PortInstance>(_ports.Values);
            _ports.Clear();
        }

        foreach (var instance in instances)
        {
            instance.Service.Close();
            instance.Service.Dispose();
        }
    }

    public void Dispose() => CloseAll();
}

public class PortInstance
{
    public string Tag { get; set; } = "";
    public ISerialService Service { get; set; } = null!;
    public SerialConfig Config { get; set; } = null!;
}
