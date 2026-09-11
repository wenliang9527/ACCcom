namespace ACCcom.ViewModels;

public class StatsViewModel : ObservableObject
{
    private string _rxBytesPerSec = "0.0";
    public string RxBytesPerSec { get => _rxBytesPerSec; set => SetField(ref _rxBytesPerSec, value); }

    private string _rxFramesPerSec = "0.0";
    public string RxFramesPerSec { get => _rxFramesPerSec; set => SetField(ref _rxFramesPerSec, value); }

    private string _txBytesPerSec = "0.0";
    public string TxBytesPerSec { get => _txBytesPerSec; set => SetField(ref _txBytesPerSec, value); }

    private string _txFramesPerSec = "0.0";
    public string TxFramesPerSec { get => _txFramesPerSec; set => SetField(ref _txFramesPerSec, value); }

    private string _errorRate = "0.0";
    public string ErrorRate { get => _errorRate; set => SetField(ref _errorRate, value); }

    private string _totalRxBytes = "0";
    public string TotalRxBytes { get => _totalRxBytes; set => SetField(ref _totalRxBytes, value); }

    private string _totalTxBytes = "0";
    public string TotalTxBytes { get => _totalTxBytes; set => SetField(ref _totalTxBytes, value); }

    private string _totalRxFrames = "0";
    public string TotalRxFrames { get => _totalRxFrames; set => SetField(ref _totalRxFrames, value); }

    private string _totalTxFrames = "0";
    public string TotalTxFrames { get => _totalTxFrames; set => SetField(ref _totalTxFrames, value); }

    private string _connectionDuration = "--";
    public string ConnectionDuration { get => _connectionDuration; set => SetField(ref _connectionDuration, value); }

    private string _avgFrameInterval = "0.0";
    public string AvgFrameInterval { get => _avgFrameInterval; set => SetField(ref _avgFrameInterval, value); }

    // TX rate tracking — sliding-window math lives in Core (TxThroughputWindow),
    // mirroring the RX side's DataStatistics; this VM only formats.
    private readonly ACCcom.Core.Services.TxThroughputWindow _txWindow = new();

    public void RecordTx(int byteCount) => _txWindow.Record(byteCount);

    public void Update(ACCcom.Core.Services.DataStatistics stats, long rxByteCount, long txByteCount, int rxCount, int txCount, string duration)
    {
        RxBytesPerSec = $"{stats.RxBytesPerSecond:F1}";
        RxFramesPerSec = $"{stats.RxFramesPerSecond:F1}";
        ErrorRate = $"{stats.ErrorRate:F1}";
        TotalRxBytes = $"{rxByteCount:N0}";
        TotalTxBytes = $"{txByteCount:N0}";
        TotalRxFrames = $"{rxCount:N0}";
        TotalTxFrames = $"{txCount:N0}";
        ConnectionDuration = string.IsNullOrEmpty(duration) ? "--" : duration;
        AvgFrameInterval = $"{stats.AvgFrameIntervalMs:F1}";

        var (bytesPerSec, framesPerSec) = _txWindow.ComputeRate(DateTime.Now);
        TxBytesPerSec = $"{bytesPerSec:F1}";
        TxFramesPerSec = $"{framesPerSec:F1}";
    }
}
