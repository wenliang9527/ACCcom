using System.Collections.ObjectModel;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

/// <summary>
/// Backs the Virtual Serial Simulator window: owns a VirtualSerialService and
/// lets the user inject RX data (simulated device responses) or send TX, with
/// every injected RX also fed into the live parse pipeline via
/// DataFlowViewModel.OnSerialData — so protocol parsers can be exercised
/// without any real hardware.
/// </summary>
public class VirtualSerialViewModel : ObservableObject, IDisposable
{
    private readonly VirtualSerialService _service = new();
    private readonly DataFlowViewModel _dataFlow;
    private readonly Action<string> _setStatus;

    private string _portName = "COM_VIRTUAL";
    private int _baudRate = 115200;
    private string _rxHexInput = "";
    private string _txInput = "";
    private bool _isHexSend;

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public VirtualSerialViewModel(DataFlowViewModel dataFlow, Action<string> setStatus)
    {
        _dataFlow = dataFlow;
        _setStatus = setStatus;

        // Local log of both directions for the simulator window.
        _service.OnDataReceived += entry => Entries.Add(entry);

        OpenCommand = new RelayCommand(_ => Open(), _ => !_service.IsOpen);
        CloseCommand = new RelayCommand(_ => Close(), _ => _service.IsOpen);
        InjectRxCommand = new RelayCommand(_ => InjectRx(), _ => !string.IsNullOrWhiteSpace(RxHexInput));
        SendCommand = new RelayCommand(_ => Send(), _ => _service.IsOpen && !string.IsNullOrEmpty(TxInput));
        ClearCommand = new RelayCommand(_ => { Entries.Clear(); _service.ClearSentData(); });
    }

    public ICommand OpenCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand InjectRxCommand { get; }
    public ICommand SendCommand { get; }
    public ICommand ClearCommand { get; }

    public bool IsOpen => _service.IsOpen;

    public string PortName
    {
        get => _portName;
        set { if (SetField(ref _portName, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public int BaudRate
    {
        get => _baudRate;
        set { if (SetField(ref _baudRate, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public string RxHexInput
    {
        get => _rxHexInput;
        set { if (SetField(ref _rxHexInput, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public string TxInput
    {
        get => _txInput;
        set { if (SetField(ref _txInput, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsHexSend
    {
        get => _isHexSend;
        set { if (SetField(ref _isHexSend, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    private void Open()
    {
        if (_service.Open(new SerialConfig { PortName = PortName, BaudRate = BaudRate, DataBits = 8, StopBits = 1, Parity = 0 }))
        {
            OnPropertyChanged(nameof(IsOpen));
            CommandManager.InvalidateRequerySuggested();
            _setStatus(string.Format(LanguageManager.Instance["Status.VirtualPortOpened"], PortName));
        }
    }

    private void Close()
    {
        _service.Close();
        OnPropertyChanged(nameof(IsOpen));
        CommandManager.InvalidateRequerySuggested();
        _setStatus(LanguageManager.Instance["Status.VirtualPortClosed"]);
    }

    private void InjectRx()
    {
        try
        {
            // Feed the simulator's local log AND the live parse pipeline (parser,
            // highlight, RX entries, protocol-test runner) in one step.
            _service.InjectRxData(RxHexInput);
            var last = Entries.Count > 0 ? Entries[^1] : null;
            if (last != null && last.Direction == "RX")
                _dataFlow.OnSerialData(last);
        }
        catch (FormatException ex)
        {
            _setStatus(string.Format(LanguageManager.Instance["Status.VirtualInjectFailed"], ex.Message));
        }
    }

    private void Send()
    {
        if (!_service.Send(TxInput, IsHexSend))
            _setStatus(LanguageManager.Instance["Status.VirtualNotOpen"]);
    }

    public void Dispose() => _service.Dispose();
}
