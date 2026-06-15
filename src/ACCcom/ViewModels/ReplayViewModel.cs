using System.IO;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class ReplayViewModel : ObservableObject
{
    private readonly SessionRecorder _sessionRecorder;
    private readonly Func<DataFlowViewModel> _getDataFlow;
    private readonly Action<string> _setStatus;
    private ReplayWindow? _replayWindow;

    public ICommand ReplayFileCommand { get; }

    public ReplayViewModel(
        SessionRecorder sessionRecorder,
        Func<DataFlowViewModel> getDataFlow,
        Action<string> setStatus)
    {
        _sessionRecorder = sessionRecorder;
        _getDataFlow = getDataFlow;
        _setStatus = setStatus;

        ReplayFileCommand = new RelayCommand(_ => ReplayFile());
    }

    private void ReplayFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSONL recordings (*.jsonl)|*.jsonl|Text logs (*.txt)|*.txt|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        _replayWindow?.Close();

        var df = _getDataFlow();
        async void OnReplayEntry(LogEntry entry)
        {
            try
            {
                entry.PortTag = "replay";
                if (entry.Direction == "RX")
                {
                    await df.RunParserAsync(entry);
                    df.AddRxEntry(entry, HexHelper.CountHexBytes(entry.RawHex ?? ""));
                }
                else
                {
                    df.AddTxEntry(entry, HexHelper.CountHexBytes(entry.RawHex ?? ""));
                }
            }
            catch (Exception ex)
            {
                _setStatus(string.Format(LanguageManager.Instance["Status.ErrorProcessingFrame"], ex.Message));
            }
        }

        _replayWindow = new ReplayWindow(dialog.FileName, _sessionRecorder, OnReplayEntry);
        _replayWindow.Owner = System.Windows.Application.Current.MainWindow;
        _replayWindow.Closed += (_, _) => _replayWindow = null;
        _replayWindow.Show();
        _setStatus(string.Format(LanguageManager.Instance["Status.ReplayWindowOpened"], Path.GetFileName(dialog.FileName)));
    }

    public void CloseWindow()
    {
        _replayWindow?.Close();
    }
}
