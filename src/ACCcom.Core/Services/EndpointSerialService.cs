using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

/// <summary>
/// Port-name-dispatching <see cref="ISerialService"/>: a "COM3"-style name
/// runs a real <see cref="SerialService"/>, a TCP endpoint
/// ("tcp://host:port" or bare "host:port") runs a
/// <see cref="TcpSerialService"/>. The transport choice is made inside the
/// service (lazily, at Open) because neither construction site can see the
/// config: the default single-port session is DI-injected as a plain
/// ISerialService, and <see cref="MultiPortService"/>'s factory takes no
/// arguments — so both register this type and the dispatch happens where the
/// config actually arrives.
///
/// Everything except Open delegates to the lazily created inner service; the
/// four events are re-raised from it, and subscribers may attach before the
/// first Open (MultiPortService wires events at construction time).
/// </summary>
public class EndpointSerialService : ISerialService, IDisposable
{
    private ISerialService? _inner;
    private readonly object _innerGate = new();

    public bool IsOpen => _inner?.IsOpen ?? false;
    public string? CurrentPort => _inner?.CurrentPort;
    public int BaudRate => _inner?.BaudRate ?? 0;
    public SerialConfig? ActiveConfig => _inner?.ActiveConfig;

    public event Action<LogEntry>? OnDataReceived;
    public event Action<string>? OnError;
    public event Action? OnDisconnected;
    public event Action<string>? OnDeviceWait;

    public bool Open(SerialConfig? config)
    {
        var inner = EnsureInner(config);
        return inner?.Open(config!) ?? false;
    }

    public async Task<bool> OpenAsync(SerialConfig? config)
    {
        var inner = EnsureInner(config);
        return inner == null ? false : await inner.OpenAsync(config!).ConfigureAwait(false);
    }

    /// <summary>Returns the inner service for this config's transport,
    /// swapping it when the transport kind changes between opens (close the
    /// old one — a service instance that was opened as COM cannot become a
    /// TCP session and vice versa). Null only for a null config.</summary>
    private ISerialService? EnsureInner(SerialConfig? config)
    {
        if (config == null) return null;
        lock (_innerGate)
        {
            var wantsTcp = TcpEndpoint.TryParse(config.PortName, out _);
            var current = _inner;
            if (current != null && (wantsTcp ? current is TcpSerialService : current is SerialService))
                return current;

            if (current != null)
            {
                current.Dispose();
                _inner = null;
            }

            ISerialService created = wantsTcp ? new TcpSerialService() : new SerialService();
            // Re-raise through this wrapper so pre-Open subscribers (event
            // wiring happens at construction time, before any Open) keep
            // working across inner creations.
            created.OnDataReceived += e => OnDataReceived?.Invoke(e);
            created.OnError += m => OnError?.Invoke(m);
            created.OnDisconnected += () => OnDisconnected?.Invoke();
            created.OnDeviceWait += m => OnDeviceWait?.Invoke(m);
            _inner = created;
            return created;
        }
    }

    // Send takes a nullable payload to mirror ISerialService.SendAsync's
    // contract (null is forwarded; the implementation's guard rejects it).
    public bool Send(string? data, bool isHex = false) => _inner?.Send(data!, isHex) ?? false;

    public async Task<bool> SendAsync(string? data, bool isHex = false)
        => _inner == null ? false : await _inner.SendAsync(data, isHex).ConfigureAwait(false);

    public bool SendHex(string hex) => _inner?.SendHex(hex) ?? false;

    /// <summary>Semantics depend on the transport: live DCB change on a real
    /// port, transparent reconnect + re-handshake on TCP.</summary>
    public bool SetBaudRate(int baudRate) => _inner?.SetBaudRate(baudRate) ?? false;

    public bool Close() => _inner?.Close() ?? true;

    public async Task<bool> CloseAsync()
        => _inner == null ? true : await _inner.CloseAsync().ConfigureAwait(false);

    public void Dispose()
    {
        lock (_innerGate)
        {
            _inner?.Dispose();
            _inner = null;
        }
    }
}
