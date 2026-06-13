using System.Collections.ObjectModel;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class ConnectionViewModel : ObservableObject, IDisposable
{
    private readonly SerialService _serial;
    private readonly NetworkBridgeService _networkBridge;
    private readonly SerialConnectionManager _connectionManager;
    private readonly Action<string> _setStatus;

    public ObservableCollection<string> AvailablePorts { get; } = new();
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

    public ICommand OpenCloseCommand { get; }
    public ICommand ConnectNetworkCommand { get; }
    public ICommand RefreshPortsCommand { get; }

    public ConnectionViewModel(SerialService serial, NetworkBridgeService networkBridge, SerialConnectionManager connectionManager, Action<string> setStatus)
    {
        _serial = serial;
        _networkBridge = networkBridge;
        _connectionManager = connectionManager;
        _setStatus = setStatus;

        OpenCloseCommand = new RelayCommand(_ => ToggleOpenClose());
        ConnectNetworkCommand = new RelayCommand(_ => ToggleNetworkConnection());
        RefreshPortsCommand = new RelayCommand(_ => RefreshPorts());

        _connectionManager.DurationChanged += duration =>
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => ConnectionDuration = duration);

        RefreshPorts();
    }

    public void RefreshPorts()
    {
        AvailablePorts.Clear();
        foreach (var p in SerialService.GetAvailablePorts())
            AvailablePorts.Add(p);
    }

    private void ToggleOpenClose()
    {
        if (IsOpen)
        {
            _connectionManager.ToggleConnection(_serial, null, true);
            IsOpen = false;
            _setStatus("Port closed");
            ConnectionDuration = "";
        }
        else
        {
            if (string.IsNullOrEmpty(SelectedPort))
            {
                _setStatus("Please select a port");
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
            _setStatus(IsOpen ? $"Connected {SelectedPort} | {SelectedBaudRate} bps" : "Open failed");
        }
    }

    private void ToggleNetworkConnection()
    {
        if (IsOpen)
        {
            _networkBridge.Close();
            IsOpen = false;
            _setStatus("Network connection closed");
        }
        else
        {
            if (string.IsNullOrEmpty(NetworkHost))
            {
                _setStatus("Please enter a host address");
                return;
            }
            if (NetworkPort <= 0)
            {
                _setStatus("Please enter a valid port");
                return;
            }

            bool connected;
            if (SelectedConnectionType == "TCP")
                connected = _networkBridge.ConnectTcp(NetworkHost, NetworkPort);
            else
                connected = _networkBridge.ConnectUdp(NetworkHost, NetworkPort);

            IsOpen = connected;
            _setStatus(connected
                ? $"Connected {SelectedConnectionType} {NetworkHost}:{NetworkPort}"
                : $"{SelectedConnectionType} connection failed");
        }
    }

    public void Dispose()
    {
        _connectionManager.Dispose();
    }
}
