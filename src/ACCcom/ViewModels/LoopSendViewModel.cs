using System.Windows.Input;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class LoopSendViewModel : ObservableObject, IDisposable
{
    private readonly ISerialService _serial;
    private readonly Func<bool> _getIsOpen;
    private readonly Func<DataFlowViewModel> _getDataFlow;
    private readonly Action<string> _setStatus;
    private bool _disposed;

    private bool _isLoopSend;
    public bool IsLoopSend
    {
        get => _isLoopSend;
        set { if (SetField(ref _isLoopSend, value)) StartLoop(); }
    }

    private int _loopInterval = 1000;
    public int LoopInterval { get => _loopInterval; set => SetField(ref _loopInterval, value); }

    private bool _isLooping;
    public bool IsLooping { get => _isLooping; set => SetField(ref _isLooping, value); }

    private System.Timers.Timer? _loopTimer;

    public ICommand StopLoopCommand { get; }

    public LoopSendViewModel(
        ISerialService serial,
        Func<bool> getIsOpen,
        Func<DataFlowViewModel> getDataFlow,
        Action<string> setStatus)
    {
        _serial = serial;
        _getIsOpen = getIsOpen;
        _getDataFlow = getDataFlow;
        _setStatus = setStatus;

        StopLoopCommand = new RelayCommand(_ => StopLoop());
    }

    private void StartLoop()
    {
        var df = _getDataFlow();
        if (!IsLoopSend || string.IsNullOrEmpty(df.SendText)) return;
        StopLoop();
        _loopTimer = new System.Timers.Timer(LoopInterval > 0 ? LoopInterval : 1000);
        _loopTimer.Elapsed += (_, _) =>
        {
            if (_getIsOpen()) _serial.Send(df.SendText, df.IsHexSend);
        };
        _loopTimer.Start();
        IsLooping = true;
        _setStatus(LanguageManager.Instance["Status.LoopSending"]);
    }

    public void StopLoop()
    {
        _loopTimer?.Stop();
        _loopTimer?.Dispose();
        _loopTimer = null;
        IsLooping = false;
        IsLoopSend = false;
        _setStatus(LanguageManager.Instance["Status.LoopStopped"]);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopLoop();
    }
}
