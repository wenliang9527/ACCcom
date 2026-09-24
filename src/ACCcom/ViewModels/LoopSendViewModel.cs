using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class LoopSendViewModel : ObservableObject, IDisposable
{
    private readonly ISerialService _serial;
    private readonly NetworkBridgeService? _networkBridge;
    private readonly Func<bool> _getIsOpen;
    private readonly Func<DataFlowViewModel> _getDataFlow;
    private readonly Func<int> _getBaudRate;
    private readonly Action<string> _setStatus;
    private bool _disposed;

    private bool _isLoopSend;
    public bool IsLoopSend
    {
        get => _isLoopSend;
        set { if (SetField(ref _isLoopSend, value)) StartLoop(); }
    }

    private int _loopInterval = 1000;
    /// <summary>Loop period in ms. Clamped up to <see cref="MinLoopInterval"/> so the
    /// UART is free before the next frame (payload length × baud).</summary>
    public int LoopInterval
    {
        get => _loopInterval;
        set
        {
            int clamped = Math.Max(MinLoopInterval, value);
            if (SetField(ref _loopInterval, clamped))
            {
                if (_loopTimer != null) _loopTimer.Interval = Math.Max(1, clamped);
                OnPropertyChanged(nameof(MinLoopIntervalText));
            }
        }
    }

    /// <summary>Floor from current send-box payload size × baud (see <see cref="SerialTiming.MinIntervalMs"/>).</summary>
    public int MinLoopInterval
    {
        get
        {
            var df = _getDataFlow();
            int bytes = HexHelper.CountSendBytes(df.SendText, df.IsHexSend);
            return SerialTiming.MinIntervalMs(bytes, _getBaudRate());
        }
    }

    public string MinLoopIntervalText =>
        string.Format(LanguageManager.Instance["QuickSend.MinInterval"], MinLoopInterval);

    private bool _isLooping;
    public bool IsLooping { get => _isLooping; set => SetField(ref _isLooping, value); }

    private System.Timers.Timer? _loopTimer;

    public ICommand StopLoopCommand { get; }

    public LoopSendViewModel(
        ISerialService serial,
        Func<bool> getIsOpen,
        Func<DataFlowViewModel> getDataFlow,
        Action<string> setStatus,
        Func<int>? getBaudRate = null,
        NetworkBridgeService? networkBridge = null)
    {
        _serial = serial;
        _networkBridge = networkBridge;
        _getIsOpen = getIsOpen;
        _getDataFlow = getDataFlow;
        _setStatus = setStatus;
        _getBaudRate = getBaudRate ?? (() => serial.BaudRate > 0 ? serial.BaudRate : SerialTiming.DefaultBaudRate);

        StopLoopCommand = new RelayCommand(_ => StopLoop());
    }

    /// <summary>Loads a payload into the send box and starts (or restarts) the shared loop sender.</summary>
    public void StartLoopFrom(string text, bool isHex)
    {
        var df = _getDataFlow();
        df.SendText = text;
        df.IsHexSend = isHex;
        OnPropertyChanged(nameof(MinLoopInterval));
        OnPropertyChanged(nameof(MinLoopIntervalText));
        // Force a fresh StartLoop even when the checkbox is already on.
        if (_isLoopSend)
        {
            _isLoopSend = false;
            OnPropertyChanged(nameof(IsLoopSend));
        }
        IsLoopSend = true;
    }

    private void StartLoop()
    {
        if (!IsLoopSend)
        {
            TearDownTimer(silent: true);
            return;
        }

        var df = _getDataFlow();
        if (string.IsNullOrEmpty(df.SendText))
        {
            _isLoopSend = false;
            OnPropertyChanged(nameof(IsLoopSend));
            return;
        }

        int min = MinLoopInterval;
        if (_loopInterval < min) LoopInterval = min;

        TearDownTimer(silent: true);
        _loopTimer = new System.Timers.Timer(Math.Max(1, _loopInterval));
        var text = df.SendText;
        var isHex = df.IsHexSend;
        _loopTimer.Elapsed += (_, _) =>
        {
            if (!_getIsOpen()) return;
            if (_networkBridge?.IsConnected == true)
                _networkBridge.Send(text, isHex);
            else
                _serial.Send(text, isHex);
        };
        _loopTimer.Start();
        IsLooping = true;
        OnPropertyChanged(nameof(MinLoopInterval));
        OnPropertyChanged(nameof(MinLoopIntervalText));
        _setStatus(LanguageManager.Instance["Status.LoopSending"]);
    }

    public void StopLoop()
    {
        TearDownTimer(silent: false);
    }

    private void TearDownTimer(bool silent)
    {
        _loopTimer?.Stop();
        _loopTimer?.Dispose();
        _loopTimer = null;
        bool was = IsLooping;
        IsLooping = false;
        _isLoopSend = false;
        OnPropertyChanged(nameof(IsLoopSend));
        if (was && !silent)
            _setStatus(LanguageManager.Instance["Status.LoopStopped"]);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopLoop();
    }
}
