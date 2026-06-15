using System.Collections.ObjectModel;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class MultiPortViewModel : ObservableObject
{
    private readonly MultiPortService _multiPort;
    private readonly Action<string> _setStatus;

    public ObservableCollection<PortItemViewModel> ConnectedPorts { get; } = new();

    private string _newPortTag = "";
    public string NewPortTag { get => _newPortTag; set => SetField(ref _newPortTag, value); }

    private string _newPortName = "";
    public string NewPortName { get => _newPortName; set => SetField(ref _newPortName, value); }

    private int _newPortBaud = 115200;
    public int NewPortBaud { get => _newPortBaud; set => SetField(ref _newPortBaud, value); }

    public ICommand OpenMultiPortCommand { get; }
    public ICommand CloseMultiPortCommand { get; }
    public ICommand CloseAllPortsCommand { get; }

    public MultiPortViewModel(
        MultiPortService multiPort,
        Action<string> setStatus)
    {
        _multiPort = multiPort;
        _setStatus = setStatus;

        OpenMultiPortCommand = new RelayCommand(_ => OpenMultiPort(), _ => !string.IsNullOrEmpty(NewPortTag) && !string.IsNullOrEmpty(NewPortName));
        CloseMultiPortCommand = new RelayCommand(p => { if (p is PortItemViewModel item) CloseMultiPort(item); });
        CloseAllPortsCommand = new RelayCommand(_ => { _multiPort.CloseAll(); ConnectedPorts.Clear(); _setStatus(LanguageManager.Instance["Status.AllAuxPortsClosed"]); });
    }

    private void OpenMultiPort()
    {
        if (ConnectedPorts.Any(p => p.Tag == NewPortTag))
        {
            _setStatus(string.Format(LanguageManager.Instance["Status.PortTagExists"], NewPortTag));
            return;
        }
        var config = new SerialConfig
        {
            PortName = NewPortName,
            BaudRate = NewPortBaud,
            DataBits = 8,
            StopBits = 1,
            Parity = 0
        };
        if (_multiPort.OpenPort(NewPortTag, config))
        {
            ConnectedPorts.Add(new PortItemViewModel
            {
                Tag = NewPortTag,
                PortName = NewPortName,
                BaudRate = NewPortBaud,
                IsOpen = true
            });
            _setStatus(string.Format(LanguageManager.Instance["Status.AuxPortOpened"], NewPortTag, NewPortName));
            NewPortTag = "";
        }
        else
        {
            _setStatus(string.Format(LanguageManager.Instance["Status.AuxPortOpenFailed"], NewPortName));
        }
    }

    private void CloseMultiPort(PortItemViewModel item)
    {
        if (_multiPort.ClosePort(item.Tag))
        {
            ConnectedPorts.Remove(item);
            _setStatus(string.Format(LanguageManager.Instance["Status.AuxPortClosed"], item.Tag));
        }
    }
}
