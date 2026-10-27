using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class ConnectionViewModel : ObservableObject, IDisposable
{
    private readonly ISerialService _serial;
    private readonly NetworkBridgeService _networkBridge;
    private readonly SerialConnectionManager _connectionManager;
    private readonly PortMonitorService? _portMonitor;
    private readonly AutoBaudDetector? _autoBaud;
    private readonly Action<string> _setStatus;
    private readonly Action<string> _durationChangedHandler;
    private bool _disposed;
    private CancellationTokenSource? _detectCts;

    public ObservableCollection<string> AvailablePorts { get; } = new();

    /// <summary>Port + friendly device description (CH340 / J-Link / FTDI ...),
    /// for the port picker. Display = "COM3 — USB-SERIAL CH340".</summary>
    public sealed record PortOption(string Name, string Description)
    {
        public string Display => string.IsNullOrEmpty(Description)
            ? Name
            : $"{Name} — {Description}";

        // UI Automation / screen readers read a ComboBoxItem's name from
        // ToString(); the record default would expose the raw property dump.
        public override string ToString() => Display;
    }

    public ObservableCollection<PortOption> PortOptions { get; } = new();

    /// <summary>TCP endpoints (tcp://host:port) remembered across sessions —
    /// the wireless DAP bridge is the motivating case. They never appear in
    /// the OS COM list, so without this the picker could only reach them by
    /// typing; kept in settings and merged into <see cref="PortOptions"/>.</summary>
    public ObservableCollection<string> KnownTcpEndpoints { get; } = new();
    public ObservableCollection<int> BaudRates { get; } = new() { 300, 600, 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 };
    public ObservableCollection<int> DataBitsList { get; } = new() { 5, 6, 7, 8 };
    public ObservableCollection<string> StopBitsList { get; } = new() { "None", "One", "Two" };
    public ObservableCollection<string> ParityList { get; } = new() { "None", "Odd", "Even" };

    private string _selectedPort = "";
    public string SelectedPort { get => _selectedPort; set => SetField(ref _selectedPort, value); }

    private int _selectedBaudRate = 115200;
    public int SelectedBaudRate { get => _selectedBaudRate; set => SetField(ref _selectedBaudRate, value); }

    private int _selectedDataBits = 8;
    public int SelectedDataBits { get => _selectedDataBits; set => SetField(ref _selectedDataBits, value); }

    private int _selectedStopBits = 1;
    public int SelectedStopBits { get => _selectedStopBits; set => SetField(ref _selectedStopBits, value); }

    private int _selectedParity = 0;
    public int SelectedParity { get => _selectedParity; set => SetField(ref _selectedParity, value); }

    private bool _dtrEnable;
    public bool DtrEnable { get => _dtrEnable; set => SetField(ref _dtrEnable, value); }

    private bool _rtsEnable;
    public bool RtsEnable { get => _rtsEnable; set => SetField(ref _rtsEnable, value); }

    private bool _autoReconnect = true;
    public bool AutoReconnect { get => _autoReconnect; set => SetField(ref _autoReconnect, value); }

    private int _reconnectIntervalMs = 3000;
    public int ReconnectIntervalMs { get => _reconnectIntervalMs; set => SetField(ref _reconnectIntervalMs, value); }

    private int _maxReconnectAttempts;
    public int MaxReconnectAttempts { get => _maxReconnectAttempts; set => SetField(ref _maxReconnectAttempts, value); }

    public ObservableCollection<string> ConnectionTypes { get; } = new() { "Serial", "TCP", "UDP" };
    public ObservableCollection<string> Languages { get; } = new() { "zh-CN", "en-US" };

    private string _selectedConnectionType = "Serial";
    public string SelectedConnectionType { get => _selectedConnectionType; set => SetField(ref _selectedConnectionType, value); }

    private string _selectedLanguage = "zh-CN";
    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (SetField(ref _selectedLanguage, value))
            {
                LanguageManager.Instance.CurrentLanguage = value;
            }
        }
    }

    private string _networkHost = "127.0.0.1";
    public string NetworkHost { get => _networkHost; set => SetField(ref _networkHost, value); }

    private int _networkPort = 4001;
    public int NetworkPort { get => _networkPort; set => SetField(ref _networkPort, value); }

    private bool _isOpen;
    public bool IsOpen { get => _isOpen; set => SetField(ref _isOpen, value); }

    private string _connectionDuration = "";
    public string ConnectionDuration { get => _connectionDuration; set => SetField(ref _connectionDuration, value); }

    /// <summary>True while the auto-baud probe is cycling through common rates.
    /// The button stays disabled (and shows "Detecting…") until it finishes.</summary>
    private bool _isDetecting;
    public bool IsDetecting
    {
        get => _isDetecting;
        private set
        {
            if (SetField(ref _isDetecting, value))
            {
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                OnPropertyChanged(nameof(DetectGlyph));
            }
        }
    }

    /// <summary>The baud rate currently being probed, e.g. "9600". Empty when idle.</summary>
    private string _detectProgressText = "";
    public string DetectProgressText { get => _detectProgressText; set => SetField(ref _detectProgressText, value); }

    /// <summary>Button face glyph: sync-arrows while probing, magnifier when idle.
    /// Rendered in Segoe MDL2 Assets (see ConnectionPanel.xaml).</summary>
    public string DetectGlyph => IsDetecting ? "\uE895" : "\uE721";

    public ICommand OpenCloseCommand { get; }
    public ICommand ConnectNetworkCommand { get; }
    public ICommand RefreshPortsCommand { get; }
    public ICommand AutoDetectBaudCommand { get; }

    public ConnectionViewModel(ISerialService serial, NetworkBridgeService networkBridge, SerialConnectionManager connectionManager, Action<string> setStatus, PortMonitorService? portMonitor = null, AutoBaudDetector? autoBaud = null)
    {
        _serial = serial;
        _networkBridge = networkBridge;
        _connectionManager = connectionManager;
        _setStatus = setStatus;
        _portMonitor = portMonitor;
        _autoBaud = autoBaud;

        OpenCloseCommand = new RelayCommand(_ => _ = ToggleOpenClose());
        ConnectNetworkCommand = new RelayCommand(_ => _ = ConnectNetworkAsync());
        RefreshPortsCommand = new RelayCommand(_ => RefreshPorts());
        AutoDetectBaudCommand = new RelayCommand(
            _ => _ = DetectBaudAsync(),
            _ => !IsDetecting && !IsOpen && !string.IsNullOrEmpty(SelectedPort) && _autoBaud != null);

        _durationChangedHandler = duration =>
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => ConnectionDuration = duration);
        _connectionManager.DurationChanged += _durationChangedHandler;

        RefreshPorts();

        // Auto-detect serial devices plugged in / unplugged at runtime.
        if (_portMonitor != null)
        {
            _portMonitor.PortsChanged += OnPortsChanged;
            _portMonitor.Start(2000);
        }
    }

    private void OnPortsChanged(List<string> arrived, List<string> removed)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                foreach (var p in arrived)
                {
                    if (!AvailablePorts.Contains(p)) AvailablePorts.Add(p);
                    _setStatus(string.Format(LanguageManager.Instance["Status.PortArrived"], p));
                }

                // Auto-select the first port that appears while nothing is chosen.
                if (arrived.Count > 0 && string.IsNullOrEmpty(SelectedPort))
                    SelectedPort = arrived[0];

                foreach (var p in removed)
                {
                    AvailablePorts.Remove(p);
                    if (string.Equals(SelectedPort, p, StringComparison.OrdinalIgnoreCase))
                        _setStatus(string.Format(LanguageManager.Instance["Status.PortRemoved"], p));
                }

                // Rebuild wholesale: a plug/unplug must never drop the
                // remembered TCP endpoints from the picker.
                RebuildPortOptions();
            }
            catch (Exception ex)
            {
                _setStatus(string.Format(LanguageManager.Instance["Status.PortMonitorError"], ex.Message));
            }
        });
    }

    private static PortOption MakePortOption(string portName)
        => new(portName, Helpers.PortDescriptionResolver.Describe(portName));

    /// <summary>Endpoints are shown above the COM ports so the wireless bridge
    /// is visible without scrolling; the description marks their transport.</summary>
    private static PortOption MakeEndpointOption(string endpoint)
        => new(endpoint, LanguageManager.Instance["Port.TcpEndpoint"]);

    /// <summary>Rebuilds <see cref="PortOptions"/> as remembered TCP endpoints
    /// followed by the live COM list. Called by both the explicit refresh and
    /// the plug/unplug monitor so neither path can drop the endpoints.</summary>
    private void RebuildPortOptions()
    {
        var selected = SelectedPort;
        var keep = PortOptions.FirstOrDefault(o => string.Equals(o.Name, selected, StringComparison.OrdinalIgnoreCase));

        PortOptions.Clear();
        foreach (var ep in KnownTcpEndpoints) PortOptions.Add(MakeEndpointOption(ep));
        foreach (var p in AvailablePorts) PortOptions.Add(MakePortOption(p));

        // Keep the previous selection when it still exists so a manual
        // refresh does not drop the user's choice.
        if (keep != null && PortOptions.Any(o => string.Equals(o.Name, keep.Name, StringComparison.OrdinalIgnoreCase)))
            SelectedPort = keep.Name;
    }

    /// <summary>Remembers a TCP endpoint so it appears in the picker from now
    /// on (most-recent first, bounded). No-op for COM names.</summary>
    public void RememberEndpoint(string? portName)
    {
        if (string.IsNullOrWhiteSpace(portName)) return;
        if (!TcpEndpoint.TryParse(portName, out var parsed)) return;

        var normalized = parsed.ToString();
        var existing = KnownTcpEndpoints.FirstOrDefault(
            e => string.Equals(e, normalized, StringComparison.OrdinalIgnoreCase));
        if (existing != null) KnownTcpEndpoints.Remove(existing);

        KnownTcpEndpoints.Insert(0, normalized);
        while (KnownTcpEndpoints.Count > MaxKnownEndpoints)
            KnownTcpEndpoints.RemoveAt(KnownTcpEndpoints.Count - 1);

        RebuildPortOptions();
    }

    /// <summary>Seeds the remembered endpoints from settings (call before the
    /// first <see cref="RefreshPorts"/>, or refresh afterwards).</summary>
    public void LoadKnownEndpoints(IEnumerable<string>? endpoints)
    {
        KnownTcpEndpoints.Clear();
        if (endpoints != null)
        {
            foreach (var ep in endpoints)
            {
                if (TcpEndpoint.TryParse(ep, out var parsed) && KnownTcpEndpoints.Count < MaxKnownEndpoints)
                    KnownTcpEndpoints.Add(parsed.ToString());
            }
        }
        RebuildPortOptions();
    }

    private const int MaxKnownEndpoints = 8;

    public void RefreshPorts()
    {
        var ports = SerialService.GetAvailablePorts();
        var selected = SelectedPort;

        AvailablePorts.Clear();
        foreach (var p in ports) AvailablePorts.Add(p);

        RebuildPortOptions();

        // Keep the previous selection when it still exists so a manual
        // refresh does not drop the user's choice.
        if (!string.IsNullOrEmpty(selected) &&
            AvailablePorts.Any(p => string.Equals(p, selected, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedPort = AvailablePorts.First(p => string.Equals(p, selected, StringComparison.OrdinalIgnoreCase));
        }
    }

    private async Task ToggleOpenClose()
    {
        if (IsOpen)
        {
            await _connectionManager.ToggleConnection(_serial, null, true);
            IsOpen = false;
            _setStatus(LanguageManager.Instance["Status.PortClosed"]);
            ConnectionDuration = "";
        }
        else
        {
            if (string.IsNullOrEmpty(SelectedPort))
            {
                _setStatus(LanguageManager.Instance["Status.PleaseSelectPort"]);
                return;
            }
            var config = new SerialConfig
            {
                PortName = SelectedPort,
                BaudRate = SelectedBaudRate,
                DataBits = SelectedDataBits,
                StopBits = SelectedStopBits,
                Parity = SelectedParity,
                DtrEnable = DtrEnable,
                RtsEnable = RtsEnable,
                Reconnect = new ReconnectSettings
                {
                    AutoReconnect = AutoReconnect,
                    ReconnectIntervalMs = ReconnectIntervalMs,
                    MaxReconnectAttempts = MaxReconnectAttempts
                }
            };
            IsOpen = await _connectionManager.ToggleConnection(_serial, config, false);
            if (IsOpen) RememberEndpoint(SelectedPort);
            _setStatus(IsOpen ? string.Format(LanguageManager.Instance["Status.ConnectedSerial"], SelectedPort, SelectedBaudRate) : LanguageManager.Instance["Status.OpenFailed"]);
        }
    }

    /// <summary>Adopts a default-session connection opened/closed outside this
    /// panel (the HTTP API path): syncs the picker fields to the applied
    /// config and drives IsOpen (title-bar badge) plus the duration timer, so
    /// the UI reflects connections it did not initiate itself. HttpService
    /// raises this on a worker thread — everything here is marshalled to the
    /// UI thread.</summary>
    public void AdoptExternalConnection(SerialConfig? config)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;

            if (config == null)
            {
                if (!IsOpen) return;
                IsOpen = false;
                _connectionManager.StopTracking();
                ConnectionDuration = "";
                _setStatus(LanguageManager.Instance["Status.PortClosed"]);
                return;
            }

            // The default session is always a serial-mode connection (a COM
            // port or a tcp:// endpoint typed into the port box), so surface
            // it in the serial fields even if the mode picker sits on TCP/UDP.
            SelectedConnectionType = "Serial";
            SelectedPort = config.PortName;
            SelectedBaudRate = config.BaudRate;
            SelectedDataBits = config.DataBits;
            SelectedStopBits = config.StopBits;
            SelectedParity = config.Parity;
            // Agent/API-opened endpoints must also land in the picker for the
            // next session (the motivating case: the wireless bridge).
            RememberEndpoint(config.PortName);
            if (!IsOpen)
            {
                IsOpen = true;
                _connectionManager.StartTracking();
            }
            _setStatus(string.Format(LanguageManager.Instance["Status.ConnectedSerial"], config.PortName, config.BaudRate));
        });
    }

    /// <summary>
    /// Connects/disconnects the TCP/UDP bridge. Blocking socket work runs on a
    /// background thread; all VM state changes stay on the UI thread.
    /// </summary>
    private async Task ConnectNetworkAsync()
    {
        try
        {
            if (IsOpen)
            {
                await Task.Run(_networkBridge.Close);
                IsOpen = false;
                _setStatus(LanguageManager.Instance["Status.NetworkClosed"]);
                return;
            }

            if (string.IsNullOrEmpty(NetworkHost))
            {
                _setStatus(LanguageManager.Instance["Status.PleaseEnterHost"]);
                return;
            }
            if (NetworkPort <= 0)
            {
                _setStatus(LanguageManager.Instance["Status.PleaseEnterValidPort"]);
                return;
            }

            bool connected;
            if (SelectedConnectionType == "TCP")
                connected = await Task.Run(async () => await _networkBridge.ConnectTcp(NetworkHost, NetworkPort));
            else
                connected = _networkBridge.ConnectUdp(NetworkHost, NetworkPort);

            IsOpen = connected;
            _setStatus(connected
                ? string.Format(LanguageManager.Instance["Status.ConnectedNetwork"], SelectedConnectionType, NetworkHost, NetworkPort)
                : string.Format(LanguageManager.Instance["Status.ConnectionFailed"], SelectedConnectionType));
        }
        catch (Exception ex)
        {
            _setStatus(string.Format(LanguageManager.Instance["Status.NetworkError"], ex.Message));
        }
    }

    /// <summary>
    /// Probes common baud rates against the selected serial port. Each rate is
    /// tried in turn; the first one that elicits a response from the device
    /// becomes the new <see cref="SelectedBaudRate"/>. Clicking the button again
    /// while a probe is running cancels it.
    /// </summary>
    private async Task DetectBaudAsync()
    {
        if (_autoBaud == null) return;

        // Second click while detecting = cancel.
        if (IsDetecting)
        {
            _detectCts?.Cancel();
            return;
        }

        if (string.IsNullOrEmpty(SelectedPort))
        {
            _setStatus(LanguageManager.Instance["Status.PleaseSelectPortFirst"]);
            return;
        }

        IsDetecting = true;
        _detectCts = new CancellationTokenSource();
        var portName = SelectedPort;
        try
        {
            foreach (var rate in AutoBaudDetector.CommonRates)
            {
                _detectCts.Token.ThrowIfCancellationRequested();
                DetectProgressText = rate.ToString();
                _setStatus(string.Format(LanguageManager.Instance["Status.BaudDetecting"], rate));

                if (await _autoBaud.TryBaudRateAsync(portName, rate, _detectCts.Token).ConfigureAwait(true))
                {
                    SelectedBaudRate = rate;
                    _setStatus(string.Format(LanguageManager.Instance["Status.BaudDetected"], rate));
                    return;
                }
            }
            _setStatus(LanguageManager.Instance["Status.BaudDetectFailed"]);
        }
        catch (OperationCanceledException)
        {
            _setStatus(LanguageManager.Instance["Status.BaudDetectCanceled"]);
        }
        catch (Exception ex)
        {
            _setStatus(string.Format(LanguageManager.Instance["Status.BaudDetectFailed"], ex.Message));
        }
        finally
        {
            DetectProgressText = "";
            IsDetecting = false;
            _detectCts?.Dispose();
            _detectCts = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _detectCts?.Cancel();
        if (_portMonitor != null)
        {
            _portMonitor.PortsChanged -= OnPortsChanged;
            _portMonitor.Stop();
        }
        _connectionManager.DurationChanged -= _durationChangedHandler;
        _connectionManager.Dispose();
    }
}
