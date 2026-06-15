using System.Windows.Input;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class ModbusConnectionViewModel : ObservableObject
{
    private readonly ModbusConnectionManager _manager;
    private readonly Func<ModbusService, bool> _onConnected;

    private bool _isTcpMode;
    public bool IsTcpMode
    {
        get => _isTcpMode;
        set
        {
            if (SetField(ref _isTcpMode, value))
                OnPropertyChanged(nameof(IsRtuMode));
        }
    }

    public bool IsRtuMode => !IsTcpMode;

    private string _host = "127.0.0.1";
    public string Host { get => _host; set => SetField(ref _host, value); }

    private int _port = 502;
    public int Port { get => _port; set => SetField(ref _port, value); }

    private string _statusText = "Select connection mode";
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    private bool _canConnect = true;
    public bool CanConnect { get => _canConnect; set => SetField(ref _canConnect, value); }

    public ICommand ConnectCommand { get; }

    public ModbusConnectionViewModel(ModbusConnectionManager manager, Func<ModbusService, bool> onConnected)
    {
        _manager = manager;
        _onConnected = onConnected;
        ConnectCommand = new RelayCommand(async _ => await ConnectAsync(), _ => CanConnect);
    }

    private async Task ConnectAsync()
    {
        CanConnect = false;
        StatusText = "Connecting...";
        try
        {
            await Task.Run(() =>
            {
                if (IsTcpMode)
                {
                    var svc = _manager.CreateTcpConnection($"tcp_{Host}_{Port}", Host, Port);
                    _onConnected(svc);
                }
                else
                {
                    _onConnected(null!);
                }
            });
        }
        catch (Exception ex)
        {
            StatusText = $"Failed: {ex.Message}";
            CanConnect = true;
        }
    }
}
