using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class DataFlowViewModel : ObservableObject
{
    private const int MaxEntries = 5000;
    private readonly SerialService _serial;
    private readonly NetworkBridgeService _networkBridge;
    private readonly LoggerService _logger;
    private readonly HttpService _http;
    private readonly TriggerService _triggerService;
    private readonly ParserManager _parserManager;
    private readonly DataStatistics _stats;
    private readonly FileExportService _fileExportService;
    private readonly Action<string> _setStatus;

    private readonly List<string> _sendHistory = new();
    private int _historyIndex = -1;
    private int _sendCounter;

    public ObservableRangeCollection<LogEntry> RxEntries { get; } = new();
    public ObservableRangeCollection<LogEntry> TxEntries { get; } = new();

    private string _sendText = "";
    public string SendText { get => _sendText; set => SetField(ref _sendText, value); }

    private bool _isHexSend;
    public bool IsHexSend { get => _isHexSend; set => SetField(ref _isHexSend, value); }

    private bool _isHexDisplayRx;
    public bool IsHexDisplayRx { get => _isHexDisplayRx; set => SetField(ref _isHexDisplayRx, value); }

    private bool _isHexDisplayTx;
    public bool IsHexDisplayTx { get => _isHexDisplayTx; set => SetField(ref _isHexDisplayTx, value); }

    private bool _enableRxTimestamp = true;
    public bool EnableRxTimestamp { get => _enableRxTimestamp; set => SetField(ref _enableRxTimestamp, value); }

    private bool _enableTxTimestamp = true;
    public bool EnableTxTimestamp { get => _enableTxTimestamp; set => SetField(ref _enableTxTimestamp, value); }

    private int _rxCount;
    public int RxCount { get => _rxCount; set => SetField(ref _rxCount, value); }

    private int _txCount;
    public int TxCount { get => _txCount; set => SetField(ref _txCount, value); }

    private int _rxByteCount;
    public int RxByteCount { get => _rxByteCount; set => SetField(ref _rxByteCount, value); }

    private int _txByteCount;
    public int TxByteCount { get => _txByteCount; set => SetField(ref _txByteCount, value); }

    private int _errorFrameCount;
    public int ErrorFrameCount { get => _errorFrameCount; set => SetField(ref _errorFrameCount, value); }

    private string _rxRate = "";
    public string RxRate { get => _rxRate; set => SetField(ref _rxRate, value); }

    private string _errorRate = "";
    public string ErrorRate { get => _errorRate; set => SetField(ref _errorRate, value); }

    private string _frameInterval = "";
    public string FrameInterval { get => _frameInterval; set => SetField(ref _frameInterval, value); }

    private string _rxFilterText = "";
    public string RxFilterText { get => _rxFilterText; set { if (SetField(ref _rxFilterText, value)) FilteredRxEntries?.Refresh(); } }

    private string _txFilterText = "";
    public string TxFilterText { get => _txFilterText; set { if (SetField(ref _txFilterText, value)) FilteredTxEntries?.Refresh(); } }

    private bool _isRegexFilter;
    public bool IsRegexFilter { get => _isRegexFilter; set { if (SetField(ref _isRegexFilter, value)) { FilteredRxEntries?.Refresh(); FilteredTxEntries?.Refresh(); } } }

    private bool _showRx = true;
    public bool ShowRx { get => _showRx; set { if (SetField(ref _showRx, value)) FilteredRxEntries?.Refresh(); } }

    private bool _showTx = true;
    public bool ShowTx { get => _showTx; set { if (SetField(ref _showTx, value)) FilteredTxEntries?.Refresh(); } }

    public ListCollectionView? FilteredRxEntries { get; private set; }
    public ListCollectionView? FilteredTxEntries { get; private set; }

    private bool _autoScrollRx = true;
    public bool AutoScrollRx { get => _autoScrollRx; set => SetField(ref _autoScrollRx, value); }

    private bool _autoScrollTx = true;
    public bool AutoScrollTx { get => _autoScrollTx; set => SetField(ref _autoScrollTx, value); }

    private string _selectedParser = ParserManager.NoParserName;
    public string SelectedParser
    {
        get => _selectedParser;
        set
        {
            if (SetField(ref _selectedParser, value))
            {
                if (!_parserManager.Activate(value))
                    _setStatus($"Parser load failed: {_parserManager.LastError}");
                else if (value != ParserManager.NoParserName)
                    _setStatus($"Parser: {value}");
            }
        }
    }

    public ObservableCollection<string> AvailableParsers => _parserManager.AvailableParsers;

    private LogEntry? _selectedEntry;
    public LogEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetField(ref _selectedEntry, value))
                OnPropertyChanged(nameof(HasFields));
        }
    }

    public bool HasFields => SelectedEntry?.Fields is { Count: > 0 };

    public Action<LogEntry>? OnRxProcessed { get; set; }
    public Action<LogEntry, int>? OnEntryProcessed { get; set; }

    public ICommand SendCommand { get; }
    public ICommand ClearRxCommand { get; }
    public ICommand ClearTxCommand { get; }
    public ICommand SaveRxCommand { get; }
    public ICommand SaveTxCommand { get; }
    public ICommand SaveRxJsonCommand { get; }
    public ICommand SaveTxJsonCommand { get; }
    public ICommand SaveRxCsvCommand { get; }
    public ICommand SaveTxCsvCommand { get; }
    public ICommand OpenParserDirCommand { get; }
    public ICommand CompareFramesCommand { get; }

    public DataFlowViewModel(
        SerialService serial,
        NetworkBridgeService networkBridge,
        LoggerService logger,
        HttpService http,
        TriggerService triggerService,
        ParserManager parserManager,
        DataStatistics stats,
        FileExportService fileExportService,
        Action<string> setStatus)
    {
        _serial = serial;
        _networkBridge = networkBridge;
        _logger = logger;
        _http = http;
        _triggerService = triggerService;
        _parserManager = parserManager;
        _stats = stats;
        _fileExportService = fileExportService;
        _setStatus = setStatus;

        SendCommand = new RelayCommand(_ => SendData());
        ClearRxCommand = new RelayCommand(_ => { RxEntries.Clear(); RxCount = 0; RxByteCount = 0; });
        ClearTxCommand = new RelayCommand(_ => { TxEntries.Clear(); TxCount = 0; TxByteCount = 0; });
        SaveRxCommand = new RelayCommand(_ => SaveToFile(RxEntries, "RX"));
        SaveTxCommand = new RelayCommand(_ => SaveToFile(TxEntries, "TX"));
        SaveRxJsonCommand = new RelayCommand(_ => SaveToJson(RxEntries, "RX"));
        SaveTxJsonCommand = new RelayCommand(_ => SaveToJson(TxEntries, "TX"));
        SaveRxCsvCommand = new RelayCommand(_ => SaveToCsv(RxEntries, "RX"));
        SaveTxCsvCommand = new RelayCommand(_ => SaveToCsv(TxEntries, "TX"));
        OpenParserDirCommand = new RelayCommand(_ => OpenParserDir());
        CompareFramesCommand = new RelayCommand(_ => OpenDiffWindow());

        FilteredRxEntries = (ListCollectionView)CollectionViewSource.GetDefaultView(RxEntries);
        FilteredRxEntries.Filter = o => FilterEntry((LogEntry)o, _rxFilterText, _isRegexFilter, _showRx);
        FilteredTxEntries = (ListCollectionView)CollectionViewSource.GetDefaultView(TxEntries);
        FilteredTxEntries.Filter = o => FilterEntry((LogEntry)o, _txFilterText, _isRegexFilter, _showTx);
    }

    public async void OnSerialData(LogEntry entry)
    {
        entry.PortTag = "main";
        _http.AddEntry(entry);
        _triggerService.Evaluate(entry);

        int byteCount = 0;
        if (!string.IsNullOrEmpty(entry.RawHex))
            byteCount = CountHexBytes(entry.RawHex);

        // Parse off UI thread first
        if (entry.Direction == "RX" && _parserManager.ActiveParserName != null)
            await RunParserAsync(entry).ConfigureAwait(false);

        // Then dispatch UI updates
        _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _logger.Write(entry);

            if (entry.Direction == "RX")
            {
                _stats.RecordRx(byteCount);
                if (HasErrorSeverity(entry.Fields))
                    _stats.RecordError();
                AddRxEntry(entry, byteCount);
                OnRxProcessed?.Invoke(entry);
            }
            else
            {
                AddTxEntry(entry, byteCount);
            }

            OnEntryProcessed?.Invoke(entry, byteCount);
        });
    }

    public void AddRxEntry(LogEntry entry, int byteCount)
    {
        RxEntries.Add(entry);
        RxEntries.TrimTo(MaxEntries);
        RxCount++;
        RxByteCount += byteCount;
    }

    public void AddTxEntry(LogEntry entry, int byteCount)
    {
        TxEntries.Add(entry);
        TxEntries.TrimTo(MaxEntries);
        TxCount++;
        TxByteCount += byteCount;
    }

    public void RecordTxBytes(int byteCount)
    {
        TxByteCount += byteCount;
    }

    public async Task RunParserAsync(LogEntry entry)
    {
        if (string.IsNullOrEmpty(entry.RawHex)) return;
        try
        {
            var data = HexStringToBytes(entry.RawHex);
            var fields = await _parserManager.Engine.ExecuteAsync(data, entry.Timestamp).ConfigureAwait(false);
            if (fields != null && fields.Count > 0)
            {
                entry.Fields = fields;
                if (HasErrorSeverity(fields))
                    ErrorFrameCount++;
            }
        }
        catch (Exception ex) { _setStatus($"Parser execution error: {ex.Message}"); }
    }

    public void SendData()
    {
        if (string.IsNullOrEmpty(SendText)) return;
        var toSend = IsHexSend ? SendText : ExpandVariables(SendText);

        bool sent;
        if (_networkBridge.IsConnected)
            sent = _networkBridge.Send(toSend, IsHexSend);
        else
            sent = _serial.Send(toSend, IsHexSend);

        if (sent)
        {
            if (_sendHistory.Count == 0 || _sendHistory[^1] != SendText)
                _sendHistory.Add(SendText);
            _historyIndex = _sendHistory.Count;
        }
    }

    public void NavigateHistory(int direction)
    {
        if (_sendHistory.Count == 0) return;
        _historyIndex += direction;
        if (_historyIndex < 0) _historyIndex = 0;
        if (_historyIndex >= _sendHistory.Count) _historyIndex = _sendHistory.Count;
        SendText = _historyIndex < _sendHistory.Count
            ? _sendHistory[_historyIndex]
            : "";
    }

    public string ExpandVariables(string input)
    {
        if (string.IsNullOrEmpty(input) || !input.Contains("{{")) return input;
        var now = DateTime.Now;
        return input
            .Replace("{{timestamp}}", now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Replace("{{date}}", now.ToString("yyyy-MM-dd"))
            .Replace("{{time}}", now.ToString("HH:mm:ss"))
            .Replace("{{counter}}", (++_sendCounter).ToString())
            .Replace("{{ticks}}", now.Ticks.ToString());
    }

    private void SaveToFile(ObservableCollection<LogEntry> entries, string tag)
    {
        if (entries.Count == 0) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"ACCCOM_{tag}_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
            _fileExportService.ExportToText(entries, dialog.FileName);
    }

    private void SaveToJson(ObservableCollection<LogEntry> entries, string tag)
    {
        if (entries.Count == 0) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"ACCCOM_{tag}_{DateTime.Now:yyyyMMdd_HHmmss}.json",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
            _fileExportService.ExportToJson(entries, dialog.FileName);
    }

    private void SaveToCsv(ObservableCollection<LogEntry> entries, string tag)
    {
        if (entries.Count == 0) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"ACCCOM_{tag}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
            FileExportService.ExportToCsv(entries, dialog.FileName);
    }

    private void OpenParserDir()
    {
        var dir = _parserManager.GetParserDir();
        if (Directory.Exists(dir))
            System.Diagnostics.Process.Start("explorer.exe", dir);
    }

    private void OpenDiffWindow()
    {
        if (SelectedEntry != null && !string.IsNullOrEmpty(SelectedEntry.RawHex))
        {
            var opposite = SelectedEntry.Direction == "RX"
                ? TxEntries.LastOrDefault(e => e.Id != SelectedEntry.Id)
                : RxEntries.LastOrDefault(e => e.Id != SelectedEntry.Id);

            if (opposite != null && !string.IsNullOrEmpty(opposite.RawHex))
            {
                new DiffWindow(SelectedEntry.RawHex, opposite.RawHex).Show();
                _setStatus($"Diff: #{SelectedEntry.Id} vs #{opposite.Id}");
                return;
            }
        }
        new DiffWindow().Show();
        _setStatus("Diff window opened - paste hex frames to compare");
    }

    private static bool FilterEntry(LogEntry entry, string filter, bool useRegex, bool showDirection)
    {
        if (!showDirection) return false;
        if (string.IsNullOrWhiteSpace(filter)) return true;
        var text = entry.Text ?? "";
        var hex = entry.RawHex ?? "";
        if (useRegex)
        {
            try
            {
                return System.Text.RegularExpressions.Regex.IsMatch(text, filter, System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    || System.Text.RegularExpressions.Regex.IsMatch(hex, filter, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            catch { return false; }
        }
        return text.AsSpan().Contains(filter.AsSpan(), StringComparison.OrdinalIgnoreCase)
            || hex.AsSpan().Contains(filter.AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasErrorSeverity(List<FieldAnnotation>? fields)
    {
        if (fields == null) return false;
        foreach (var f in fields)
            if (f.Severity == FieldSeverity.Error) return true;
        return false;
    }

    public static int CountHexBytes(string hex)
    {
        int count = 0;
        foreach (var c in hex.AsSpan())
            if (c != ' ') count++;
        return count / 2;
    }

    public static byte[] HexStringToBytes(string hex)
    {
        int nonSpaceLen = 0;
        foreach (var c in hex.AsSpan())
            if (c != ' ') nonSpaceLen++;
        var bytes = new byte[nonSpaceLen / 2];
        int byteIdx = 0;
        int hi = -1;
        foreach (var c in hex.AsSpan())
        {
            if (c == ' ') continue;
            int val = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'A' and <= 'F' => c - 'A' + 10,
                >= 'a' and <= 'f' => c - 'a' + 10,
                _ => 0
            };
            if (hi < 0) hi = val;
            else { bytes[byteIdx++] = (byte)(hi << 4 | val); hi = -1; }
        }
        return bytes;
    }
}
