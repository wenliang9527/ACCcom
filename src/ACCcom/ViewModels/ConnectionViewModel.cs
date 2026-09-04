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
    }

    public ObservableCollection<PortOption> PortOptions { get; } = new();
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

        OpenCloseCommand = new RelayCommand(_ => ToggleOpenClose());
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
                    if (!PortOptions.Any(o => string.Equals(o.Name, p, StringComparison.OrdinalIgnoreCase)))
                        PortOptions.Add(MakePortOption(p));
                    _setStatus(string.Format(LanguageManager.Instance["Status.PortArrived"], p));
                }

                // Auto-select the first port that appears while nothing is chosen.
                if (arrived.Count > 0 && string.IsNullOrEmpty(SelectedPort))
                    SelectedPort = arrived[0];

                foreach (var p in removed)
                {
                    AvailablePorts.Remove(p);
                    var option = PortOptions.FirstOrDefault(o => string.Equals(o.Name, p, StringComparison.OrdinalIgnoreCase));
                    if (option != null) PortOptions.Remove(option);
                    if (string.Equals(SelectedPort, p, StringComparison.OrdinalIgnoreCase))
                        _setStatus(string.Format(LanguageManager.Instance["Status.PortRemoved"], p));
                }
            }
            catch (Exception ex)
            {
                _setStatus($"[PortMonitor] {ex.Message}");
            }
        });
    }

    private static PortOption MakePortOption(string portName)
        => new(portName, Helpers.PortDescriptionResolver.Describe(portName));

    public void RefreshPorts()
    {
        var ports = SerialService.GetAvailablePorts();
        var selected = SelectedPort;

        AvailablePorts.Clear();
        PortOptions.Clear();
        foreach (var p in ports)
        {
            AvailablePorts.Add(p);
            PortOptions.Add(MakePortOption(p));
        }

        // Keep the previous selection when it still exists so a manual
        // refresh does not drop the user's choice.
        if (!string.IsNullOrEmpty(selected) &&
            ports.Any(p => string.Equals(p, selected, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedPort = ports.First(p => string.Equals(p, selected, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void ToggleOpenClose()
    {
        if (IsOpen)
        {
            _connectionManager.ToggleConnection(_serial, null, true);
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
            IsOpen = _connectionManager.ToggleConnection(_serial, config, false);
            _setStatus(IsOpen ? string.Format(LanguageManager.Instance["Status.ConnectedSerial"], SelectedPort, SelectedBaudRate) : LanguageManager.Instance["Status.OpenFailed"]);
        }
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
            _setStatus($"Network error: {ex.Message}");
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
