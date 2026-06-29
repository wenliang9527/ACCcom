using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class DataFlowViewModel : ObservableObject, IDisposable
{
    private bool _disposed;
    private int MaxEntries => _settings?.MaxDisplayEntries ?? 10000;
    private readonly ISerialService _serial;
    private readonly NetworkBridgeService _networkBridge;
    private readonly LoggerService _logger;
    private readonly HttpService _http;
    private readonly TriggerService _triggerService;
    private readonly ParserManager _parserManager;
    private readonly FrameAssembler _frameAssembler;
    private readonly FrameBuffer _frameBuffer;
    private readonly AutoParserMatcher _autoMatcher;
    private readonly DataStatistics _stats;
    private readonly FileExportService _fileExportService;
    private readonly Action<string> _setStatus;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer? _filterDebounce;

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

    public ICommand ToggleHexDisplayCommand { get; }

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
    public string RxFilterText { get => _rxFilterText; set { if (SetField(ref _rxFilterText, value)) DebounceFilter(); } }

    private string _txFilterText = "";
    public string TxFilterText { get => _txFilterText; set { if (SetField(ref _txFilterText, value)) DebounceFilter(); } }

    private bool _isRegexFilter;
    public bool IsRegexFilter { get => _isRegexFilter; set { if (SetField(ref _isRegexFilter, value)) { FilteredRxEntries?.Refresh(); FilteredTxEntries?.Refresh(); } } }

    private bool _showRx = true;
    public bool ShowRx { get => _showRx; set { if (SetField(ref _showRx, value)) FilteredRxEntries?.Refresh(); } }

    private void DebounceFilter()
    {
        if (_filterDebounce == null) return;
        _filterDebounce.IsEnabled = false;
        _filterDebounce.IsEnabled = true;
    }

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
                    _setStatus(string.Format(LanguageManager.Instance["Status.ParserLoadFailed"], _parserManager.LastError));
                else if (value != ParserManager.NoParserName)
                    _setStatus(string.Format(LanguageManager.Instance["Status.ParserSelected"], value));
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
        ISerialService serial,
        NetworkBridgeService networkBridge,
        LoggerService logger,
        HttpService http,
        TriggerService triggerService,
        ParserManager parserManager,
        FrameAssemblerConfig frameAssemblerConfig,
        DataStatistics stats,
        FileExportService fileExportService,
        Action<string> setStatus,
        AppSettings settings)
    {
        _serial = serial;
        _networkBridge = networkBridge;
        _logger = logger;
        _http = http;
        _triggerService = triggerService;
        _parserManager = parserManager;
        _frameAssembler = new FrameAssembler(frameAssemblerConfig, parserManager);
        _frameAssembler.OnFrameAssembled += OnAssembledFrame;
        _stats = stats;
        _fileExportService = fileExportService;
        _setStatus = setStatus;
        _settings = settings;

        _autoMatcher = new AutoParserMatcher();
        LoadParserFingerprints();
        _parserManager.OnParserReloaded += _ => LoadParserFingerprints();

        var bufferConfig = new FrameBufferConfig
        {
            Strategy = FrameExtractStrategy.ByHeader,
            Header = new byte[] { 0xA5, 0x5A },
            LengthFieldOffset = 2,
            LengthFieldSize = 1,
            LengthFieldIncludes = 4,
            MaxFrameSize = 4096,
            BufferCapacity = 65536,
            PartialFrameTimeoutMs = 2000
        };
        _frameBuffer = new FrameBuffer(bufferConfig, _autoMatcher, _parserManager);
        _frameBuffer.OnFrameAssembled += OnFrameReady;
        _frameBuffer.OnError += msg => _setStatus(msg);

        _filterDebounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200),
            IsEnabled = false
        };
        _filterDebounce.Tick += (_, _) =>
        {
            _filterDebounce.IsEnabled = false;
            FilteredRxEntries?.Refresh();
            FilteredTxEntries?.Refresh();
        };

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
        ToggleHexDisplayCommand = new RelayCommand(_ => { IsHexDisplayRx = !IsHexDisplayRx; IsHexDisplayTx = !IsHexDisplayTx; });

        FilteredRxEntries = (ListCollectionView)CollectionViewSource.GetDefaultView(RxEntries);
        FilteredRxEntries.Filter = o => FilterEntry((LogEntry)o, _rxFilterText, _isRegexFilter, _showRx);
        FilteredTxEntries = (ListCollectionView)CollectionViewSource.GetDefaultView(TxEntries);
        FilteredTxEntries.Filter = o => FilterEntry((LogEntry)o, _txFilterText, _isRegexFilter, _showTx);
    }

    public void OnSerialData(LogEntry entry)
    {
        try
        {
            if (string.IsNullOrEmpty(entry.PortTag))
                entry.PortTag = "main";

            if (_frameAssembler.IsEnabled)
            {
                _frameAssembler.Feed(entry);
                return;
            }

            _http.AddEntry(entry);
            _triggerService.Evaluate(entry);

            int byteCount = 0;
            if (!string.IsNullOrEmpty(entry.RawHex))
            {
                byteCount = HexHelper.CountHexBytes(entry.RawHex);
                try
                {
                    var bytes = HexHelper.HexStringToBytes(entry.RawHex);
                    if (bytes.Length > 0)
                        _frameBuffer.Write(bytes);
                }
                catch { }
            }

            if (entry.Direction == "RX")
            {
                _stats.RecordRx(byteCount);
                AddRxEntry(entry, byteCount);
            }
            else
            {
                AddTxEntry(entry, byteCount);
            }
        }
        catch (Exception ex)
        {
            _setStatus(string.Format(LanguageManager.Instance["Status.ErrorProcessingData"], ex.Message));
        }
    }

    private static byte[]? ExtractHexBytesFromText(string text)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(text, @"0x([0-9A-Fa-f]{2})");
        if (matches.Count == 0) return null;

        var bytes = new byte[matches.Count];
        for (int i = 0; i < matches.Count; i++)
            bytes[i] = Convert.ToByte(matches[i].Groups[1].Value, 16);
        return bytes;
    }

    private void OnAssembledFrame(LogEntry entry)
    {
        try
        {
            if (string.IsNullOrEmpty(entry.PortTag))
                entry.PortTag = "main";
            _http.AddEntry(entry);
            _triggerService.Evaluate(entry);

            int byteCount = 0;
            if (!string.IsNullOrEmpty(entry.RawHex))
                byteCount = HexHelper.CountHexBytes(entry.RawHex);

            _ = ProcessAssembledFrameAsync(entry, byteCount);
        }
        catch (Exception ex)
        {
            _setStatus(string.Format(LanguageManager.Instance["Status.ErrorProcessingFrame"], ex.Message));
        }
    }

    private void OnFrameReady(LogEntry entry)
    {
        try
        {
            if (string.IsNullOrEmpty(entry.PortTag))
                entry.PortTag = "main";

            _http.AddEntry(entry);
            _triggerService.Evaluate(entry);

            int byteCount = 0;
            if (!string.IsNullOrEmpty(entry.RawHex))
                byteCount = HexHelper.CountHexBytes(entry.RawHex);

            _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    _logger.Write(entry);

                    if (entry.Direction == "RX")
                    {
                        _stats.RecordRx(byteCount);
                        if (HexHelper.HasErrorSeverity(entry.Fields))
                            _stats.RecordError();
                        AddRxEntry(entry, byteCount);
                        OnRxProcessed?.Invoke(entry);
                    }
                    else
                    {
                        AddTxEntry(entry, byteCount);
                    }

                    OnEntryProcessed?.Invoke(entry, byteCount);
                }
                catch (Exception ex)
                {
                    _setStatus(string.Format(LanguageManager.Instance["Status.ErrorProcessingData"], ex.Message));
                }
            });
        }
        catch (Exception ex)
        {
            _setStatus(string.Format(LanguageManager.Instance["Status.ErrorProcessingFrame"], ex.Message));
        }
    }

    private async Task ProcessSerialEntryAsync(LogEntry entry, int byteCount)
    {
        await ProcessEntryAsync(entry, byteCount, forceRx: false, errorContext: "Status.ErrorProcessingData").ConfigureAwait(false);
    }

    private async Task ProcessAssembledFrameAsync(LogEntry entry, int byteCount)
    {
        await ProcessEntryAsync(entry, byteCount, forceRx: true, errorContext: "Status.ErrorProcessingFrame").ConfigureAwait(false);
    }

    private async Task ProcessEntryAsync(LogEntry entry, int byteCount, bool forceRx, string errorContext)
    {
        try
        {
            bool isRx = forceRx || entry.Direction == "RX";
            if (isRx && _parserManager.ActiveParserName != null)
                await RunParserAsync(entry).ConfigureAwait(false);

            _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    _logger.Write(entry);

                    if (isRx)
                    {
                        _stats.RecordRx(byteCount);
                        if (HexHelper.HasErrorSeverity(entry.Fields))
                            _stats.RecordError();
                        AddRxEntry(entry, byteCount);
                        OnRxProcessed?.Invoke(entry);
                    }
                    else
                    {
                        AddTxEntry(entry, byteCount);
                    }

                    OnEntryProcessed?.Invoke(entry, byteCount);
                }
                catch (Exception ex)
                {
                    _setStatus(string.Format(LanguageManager.Instance[errorContext], ex.Message));
                }
            });
        }
        catch (Exception ex)
        {
            _setStatus(string.Format(LanguageManager.Instance[errorContext], ex.Message));
        }
    }

    public void AddRxEntry(LogEntry entry, int byteCount)
    {
        RxEntries.Add(entry);
        if (RxEntries.Count > MaxEntries)
            RxEntries.TrimTo(MaxEntries);
        RxCount++;
        RxByteCount += byteCount;
    }

    public void AddTxEntry(LogEntry entry, int byteCount)
    {
        TxEntries.Add(entry);
        if (TxEntries.Count > MaxEntries)
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
            var data = HexHelper.HexStringToBytes(entry.RawHex);
            var fields = await _parserManager.Engine.ExecuteAsync(data, entry.Timestamp).ConfigureAwait(false);
            if (fields != null && fields.Count > 0)
            {
                entry.Fields = fields;
                if (HexHelper.HasErrorSeverity(fields))
                    ErrorFrameCount++;
            }
        }
        catch (Exception ex) { _setStatus(string.Format(LanguageManager.Instance["Status.ParserExecError"], ex.Message)); }
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
                _setStatus(string.Format(LanguageManager.Instance["Status.DiffOpened"], SelectedEntry.Id, opposite.Id));
                return;
            }
        }
        new DiffWindow().Show();
        _setStatus(LanguageManager.Instance["Status.DiffWindowOpened"]);
    }

    private static bool FilterEntry(LogEntry entry, string filter, bool useRegex, bool showDirection)
    {
        if (!showDirection) return false;
        if (string.IsNullOrWhiteSpace(filter))
        {
            entry.IsSearchMatch = false;
            return true;
        }
        var text = entry.Text ?? "";
        var hex = entry.RawHex ?? "";
        bool matches;
        if (useRegex)
        {
            try
            {
                matches = System.Text.RegularExpressions.Regex.IsMatch(text, filter, System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    || System.Text.RegularExpressions.Regex.IsMatch(hex, filter, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            catch (Exception regexEx) { Debug.WriteLine($"Regex filter error: {regexEx.Message}"); matches = false; }
        }
        else
        {
            matches = text.AsSpan().Contains(filter.AsSpan(), StringComparison.OrdinalIgnoreCase)
                || hex.AsSpan().Contains(filter.AsSpan(), StringComparison.OrdinalIgnoreCase);
        }
        entry.IsSearchMatch = matches;
        return matches;
    }

    public string GetFormattedCopyText(ObservableCollection<LogEntry> entries, string direction)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var entry in entries)
        {
            var hex = entry.RawHex ?? "";
            var text = entry.Text ?? "";
            var time = entry.Timestamp.ToString("HH:mm:ss.fff");
            if (!string.IsNullOrEmpty(hex))
                sb.AppendLine($"[{time}][{direction}][HEX] {hex}");
            if (!string.IsNullOrEmpty(text))
                sb.AppendLine($"[{time}][{direction}][TXT] {text}");
        }
        return sb.ToString();
    }

    private void LoadParserFingerprints()
    {
        _autoMatcher.Clear();
        foreach (var parserName in _parserManager.AvailableParsers)
        {
            if (parserName == ParserManager.NoParserName)
                continue;

            var fingerprint = _parserManager.GetFingerprint(parserName);
            if (fingerprint != null)
                _autoMatcher.UpdateFingerprint(parserName, fingerprint);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _frameAssembler.Dispose();
        _frameBuffer.Dispose();
        _filterDebounce?.Stop();
    }
}
