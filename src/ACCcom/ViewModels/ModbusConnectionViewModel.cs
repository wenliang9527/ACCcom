using System.Windows.Input;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class ModbusConnectionViewModel : ObservableObject
{
    private readonly ModbusConnectionManager _manager;
    private readonly ISerialService _serial;
    private readonly Action<ModbusService?>? _onConnected;

    private string _transportMode = "RTU";

    /// <summary>Three mutually exclusive radio modes (RTU over the current
    /// serial port, TCP over the network, ASCII over the current serial port).
    /// Each setter is guarded so the three booleans stay in lockstep.</summary>
    public bool IsRtuMode
    {
        get => _transportMode == "RTU";
        set { if (value && _transportMode != "RTU") { _transportMode = "RTU"; NotifyTransportChanged(); } }
    }

    public bool IsTcpMode
    {
        get => _transportMode == "TCP";
        set { if (value && _transportMode != "TCP") { _transportMode = "TCP"; NotifyTransportChanged(); } }
    }

    public bool IsAsciiMode
    {
        get => _transportMode == "ASCII";
        set { if (value && _transportMode != "ASCII") { _transportMode = "ASCII"; NotifyTransportChanged(); } }
    }

    private void NotifyTransportChanged()
    {
        OnPropertyChanged(nameof(IsRtuMode));
        OnPropertyChanged(nameof(IsTcpMode));
        OnPropertyChanged(nameof(IsAsciiMode));
    }

    private string _host = "127.0.0.1";
    public string Host { get => _host; set => SetField(ref _host, value); }

    private int _port = 502;
    public int Port { get => _port; set => SetField(ref _port, value); }

    private string _statusText = LanguageManager.Instance["ModbusConnection.StatusSelect"];
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    private bool _canConnect = true;
    public bool CanConnect { get => _canConnect; set => SetField(ref _canConnect, value); }

    public ICommand ConnectCommand { get; }

    public ModbusConnectionViewModel(ModbusConnectionManager manager, ISerialService serial, Action<ModbusService?>? onConnected)
    {
        _manager = manager;
        _serial = serial;
        _onConnected = onConnected;
        ConnectCommand = new RelayCommand(async _ => await ConnectAsync(), _ => CanConnect);
    }

    private async Task ConnectAsync()
    {
        CanConnect = false;
        StatusText = LanguageManager.Instance["ModbusConnection.StatusConnecting"];
        try
        {
            await Task.Run(() =>
            {
                if (IsTcpMode)
                {
                    var svc = _manager.CreateTcpConnection($"tcp_{Host}_{Port}", Host, Port);
                    _onConnected?.Invoke(svc);
                }
                else if (IsAsciiMode)
                {
                    _onConnected?.Invoke(_manager.CreateAsciiConnection("ascii", _serial));
                }
                else
                {
                    _onConnected?.Invoke(null);
                }
            });
        }
        catch (Exception ex)
        {
            StatusText = string.Format(LanguageManager.Instance["ModbusConnection.StatusFailed"], ex.Message);
            CanConnect = true;
        }
    }
}
