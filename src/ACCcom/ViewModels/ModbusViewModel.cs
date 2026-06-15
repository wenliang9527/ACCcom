using System.Collections.ObjectModel;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class RegisterItem
{
    public ushort Address { get; set; }
    public ushort Value { get; set; }
    public string Hex => $"0x{Value:X4}";
    public ushort Dec => Value;
    public string Binary => Convert.ToString(Value, 2).PadLeft(16, '0');
}

public class TransactionLogItem
{
    public DateTime Timestamp { get; set; }
    public ModbusFunctionCode FunctionCode { get; set; }
    public byte SlaveId { get; set; }
    public string RequestHex { get; set; } = "";
    public string ResponseHex { get; set; } = "";
    public string Status { get; set; } = "";
}

public class ModbusViewModel : ObservableObject, IDisposable
{
    private readonly ModbusService _modbus;
    private readonly Action<string> _setStatus;
    private System.Timers.Timer? _pollTimer;
    private bool _disposed;

    public ObservableCollection<RegisterItem> Registers { get; } = new();
    public ObservableCollection<TransactionLogItem> TransactionLog { get; } = new();

    private byte _slaveId = 0x01;
    public byte SlaveId { get => _slaveId; set => SetField(ref _slaveId, value); }

    private int _selectedFunctionIndex;
    public int SelectedFunctionIndex
    {
        get => _selectedFunctionIndex;
        set { if (SetField(ref _selectedFunctionIndex, value)) SelectedFunction = GetFunctionByIndex(value); }
    }

    private ModbusFunctionCode _selectedFunction = ModbusFunctionCode.ReadHoldingRegisters;
    public ModbusFunctionCode SelectedFunction
    {
        get => _selectedFunction;
        set => SetField(ref _selectedFunction, value);
    }

    public List<string> FunctionNames { get; } =
    [
        "01 Read Coils",
        "02 Read Discrete Inputs",
        "03 Read Holding Registers",
        "04 Read Input Registers",
        "05 Write Single Coil",
        "06 Write Single Register",
        "15 Write Multiple Coils",
        "16 Write Multiple Registers",
        "22 Mask Write Register",
        "23 Read/Write Multiple Registers"
    ];

    private ushort _startAddress;
    public ushort StartAddress { get => _startAddress; set => SetField(ref _startAddress, value); }

    private ushort _quantity = 10;
    public ushort Quantity { get => _quantity; set => SetField(ref _quantity, value); }

    private ushort _writeValue;
    public ushort WriteValue { get => _writeValue; set => SetField(ref _writeValue, value); }

    private string _batchValues = "";
    public string BatchValues { get => _batchValues; set => SetField(ref _batchValues, value); }

    private bool _isPolling;
    public bool IsPolling { get => _isPolling; set => SetField(ref _isPolling, value); }

    private int _pollIntervalMs = 1000;
    public int PollIntervalMs { get => _pollIntervalMs; set => SetField(ref _pollIntervalMs, value); }

    private string _statusText = "";
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    private bool _isReadFunction;
    public bool IsReadFunction { get => _isReadFunction; set => SetField(ref _isReadFunction, value); }

    public ICommand ReadCommand { get; }
    public ICommand WriteCommand { get; }
    public ICommand StartPollCommand { get; }
    public ICommand StopPollCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ExportTxtCommand { get; }

    public ModbusViewModel(ModbusService modbus, Action<string> setStatus)
    {
        _modbus = modbus;
        _setStatus = setStatus;

        _modbus.OnTransaction += OnTransaction;

        ReadCommand = new RelayCommand(async _ => await ReadAsync(), _ => true);
        WriteCommand = new RelayCommand(async _ => await WriteAsync(), _ => !IsReadFunction);
        StartPollCommand = new RelayCommand(_ => StartPoll(), _ => !IsPolling);
        StopPollCommand = new RelayCommand(_ => StopPoll(), _ => IsPolling);
        ClearLogCommand = new RelayCommand(_ => TransactionLog.Clear());
        ExportCsvCommand = new RelayCommand(_ => ExportLog("CSV"));
        ExportJsonCommand = new RelayCommand(_ => ExportLog("JSON"));
        ExportTxtCommand = new RelayCommand(_ => ExportLog("TXT"));
        CreateSlaveCommand = new RelayCommand(_ => CreateSlave());
        RemoveSlaveCommand = new RelayCommand(_ => RemoveSlave(), _ => !string.IsNullOrEmpty(SelectedSlaveId));

        SelectedFunctionIndex = 2;
    }

    private ModbusFunctionCode GetFunctionByIndex(int index) => index switch
    {
        0 => ModbusFunctionCode.ReadCoils,
        1 => ModbusFunctionCode.ReadDiscreteInputs,
        2 => ModbusFunctionCode.ReadHoldingRegisters,
        3 => ModbusFunctionCode.ReadInputRegisters,
        4 => ModbusFunctionCode.WriteSingleCoil,
        5 => ModbusFunctionCode.WriteSingleRegister,
        6 => ModbusFunctionCode.WriteMultipleCoils,
        7 => ModbusFunctionCode.WriteMultipleRegisters,
        8 => ModbusFunctionCode.MaskWriteRegister,
        9 => ModbusFunctionCode.ReadWriteMultipleRegisters,
        _ => ModbusFunctionCode.ReadHoldingRegisters
    };

    private bool IsWriteMultiple =>
        SelectedFunction == ModbusFunctionCode.WriteMultipleCoils ||
        SelectedFunction == ModbusFunctionCode.WriteMultipleRegisters;

    private static void AppendRegisters(ObservableCollection<RegisterItem> registers, byte[] data, ushort baseAddr)
    {
        if (data.Length < 2) return;
        for (int i = 0; i + 1 < data.Length; i += 2)
        {
            var addr = (ushort)(baseAddr + (i / 2));
            var value = (ushort)((data[i] << 8) | data[i + 1]);
            registers.Add(new RegisterItem { Address = addr, Value = value });
        }
    }

    public async Task ReadAsync()
    {
        StatusText = "Reading...";
        Registers.Clear();
        var ranges = ModbusUtils.MergeRanges(StartAddress, Quantity);
        foreach (var (start, count) in ranges)
        {
            ModbusResponse? result = null;
            try
            {
                result = SelectedFunction switch
                {
                    ModbusFunctionCode.ReadCoils => await _modbus.ReadCoilsAsync(SlaveId, start, count),
                    ModbusFunctionCode.ReadDiscreteInputs => await _modbus.ReadDiscreteInputsAsync(SlaveId, start, count),
                    ModbusFunctionCode.ReadHoldingRegisters => await _modbus.ReadHoldingRegistersAsync(SlaveId, start, count),
                    ModbusFunctionCode.ReadInputRegisters => await _modbus.ReadInputRegistersAsync(SlaveId, start, count),
                    _ => await _modbus.ReadHoldingRegistersAsync(SlaveId, start, count)
                };

                if (result.IsError)
                {
                    StatusText = $"Error at 0x{start:X4}: {result.ErrorMessage}";
                    return;
                }
                AppendRegisters(Registers, result.Data, start);
            }
            catch (Exception ex)
            {
                StatusText = $"Exception at 0x{start:X4}: {ex.Message}";
                return;
            }
        }
        StatusText = $"Read {Quantity} registers OK ({ranges.Count} request(s))";
    }

    public async Task WriteAsync()
    {
        StatusText = "Writing...";
        ModbusResponse? result = null;
        try
        {
            result = SelectedFunction switch
            {
                ModbusFunctionCode.WriteSingleCoil => await _modbus.WriteSingleCoilAsync(SlaveId, StartAddress, WriteValue != 0),
                ModbusFunctionCode.WriteSingleRegister => await _modbus.WriteSingleRegisterAsync(SlaveId, StartAddress, WriteValue),
                ModbusFunctionCode.WriteMultipleCoils => await _modbus.WriteMultipleCoilsAsync(SlaveId, StartAddress, ParseCoilValues(BatchValues)),
                ModbusFunctionCode.WriteMultipleRegisters => await _modbus.WriteMultipleRegistersAsync(SlaveId, StartAddress, ParseRegisterValues(BatchValues)),
                ModbusFunctionCode.MaskWriteRegister => await _modbus.MaskWriteRegisterAsync(SlaveId, StartAddress, WriteValue, (ushort)(WriteValue ^ 0xFFFF)),
                ModbusFunctionCode.ReadWriteMultipleRegisters => await _modbus.ReadWriteMultipleRegistersAsync(SlaveId, StartAddress, Quantity, StartAddress, ParseRegisterValues(BatchValues)),
                _ => await _modbus.WriteSingleRegisterAsync(SlaveId, StartAddress, WriteValue)
            };

            StatusText = result.IsError ? $"Error: {result.ErrorMessage}" : "Write OK";
        }
        catch (Exception ex)
        {
            StatusText = $"Exception: {ex.Message}";
        }
    }

    private static bool[] ParseCoilValues(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return [];
        return input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s is "1" or "true" or "on" or "yes").ToArray();
    }

    private static ushort[] ParseRegisterValues(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return [];
        return input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? ushort.Parse(s[2..], System.Globalization.NumberStyles.HexNumber)
                : ushort.TryParse(s, out var v) ? v : (ushort)0)
            .ToArray();
    }

    private void OnTransaction(ModbusTransaction tx)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            TransactionLog.Insert(0, new TransactionLogItem
            {
                Timestamp = tx.Timestamp,
                FunctionCode = tx.FunctionCode,
                SlaveId = tx.SlaveId,
                RequestHex = tx.RequestHex,
                ResponseHex = tx.ResponseHex ?? "(timeout)",
                Status = tx.IsSuccess ? "OK" : $"ERR: {tx.ErrorMessage}"
            });
        });
    }

    private void StartPoll()
    {
        if (IsPolling) return;
        StopPoll();
        _pollTimer = new System.Timers.Timer(PollIntervalMs > 0 ? PollIntervalMs : 1000);
        _pollTimer.Elapsed += async (_, _) => await ReadAsync();
        _pollTimer.Start();
        IsPolling = true;
        StatusText = $"Polling every {PollIntervalMs}ms";
    }

    private void StopPoll()
    {
        _pollTimer?.Stop();
        _pollTimer?.Dispose();
        _pollTimer = null;
        IsPolling = false;
    }

    private void ExportLog(string format)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"MODBUS_Log_{DateTime.Now:yyyyMMdd_HHmmss}",
            DefaultExt = format.ToLower(),
            Filter = format switch
            {
                "CSV" => "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                "JSON" => "JSON files (*.json)|*.json|All files (*.*)|*.*",
                "TXT" => "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                _ => "All files (*.*)|*.*"
            }
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var items = TransactionLog.ToList();
            string content = format switch
            {
                "CSV" => ExportCsv(items),
                "JSON" => ExportJson(items),
                "TXT" => ExportTxt(items),
                _ => ""
            };
            System.IO.File.WriteAllText(dialog.FileName, content);
            StatusText = $"Exported {items.Count} records to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusText = $"Export failed: {ex.Message}";
        }
    }

    private static string ExportCsv(List<TransactionLogItem> items)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Timestamp,SlaveId,FunctionCode,RequestHex,ResponseHex,Status");
        foreach (var item in items)
        {
            sb.AppendLine($"{item.Timestamp:yyyy-MM-dd HH:mm:ss},{item.SlaveId},{item.FunctionCode},{EscapeCsv(item.RequestHex)},{EscapeCsv(item.ResponseHex)},{item.Status}");
        }
        return sb.ToString();
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static string ExportJson(List<TransactionLogItem> items)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[");
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            sb.AppendLine("  {");
            sb.AppendLine($"    \"timestamp\": \"{item.Timestamp:yyyy-MM-dd HH:mm:ss}\",");
            sb.AppendLine($"    \"slaveId\": {item.SlaveId},");
            sb.AppendLine($"    \"functionCode\": \"{item.FunctionCode}\",");
            sb.AppendLine($"    \"requestHex\": \"{item.RequestHex}\",");
            sb.AppendLine($"    \"responseHex\": \"{item.ResponseHex ?? ""}\",");
            sb.AppendLine($"    \"status\": \"{item.Status}\"");
            sb.Append(i < items.Count - 1 ? "  }," : "  }");
            sb.AppendLine();
        }
        sb.AppendLine("]");
        return sb.ToString();
    }

    private static string ExportTxt(List<TransactionLogItem> items)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== MODBUS Transaction Log ===");
        sb.AppendLine($"Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Total Records: {items.Count}");
        sb.AppendLine(new string('=', 80));
        sb.AppendLine();
        foreach (var item in items)
        {
            sb.AppendLine($"Time:     {item.Timestamp:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Slave ID: {item.SlaveId}");
            sb.AppendLine($"Function: {item.FunctionCode}");
            sb.AppendLine($"Request:  {item.RequestHex}");
            sb.AppendLine($"Response: {item.ResponseHex ?? "(timeout)"}");
            sb.AppendLine($"Status:   {item.Status}");
            sb.AppendLine(new string('-', 60));
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopPoll();
        _modbus.OnTransaction -= OnTransaction;
    }

    // ===== Slave Mode =====
    private ModbusSlaveService? _slaveService;
    private string _selectedSlaveId = "";
    public string SelectedSlaveId { get => _selectedSlaveId; set => SetField(ref _selectedSlaveId, value); }
    public ObservableCollection<SlaveInfo> SlaveDevices { get; } = new();
    public ICommand CreateSlaveCommand { get; }
    public ICommand RemoveSlaveCommand { get; }

    public void SetSlaveService(ModbusSlaveService? service) { _slaveService = service; }

    private void RemoveSlave()
    {
        if (string.IsNullOrEmpty(SelectedSlaveId) || _slaveService == null) return;
        _slaveService.RemoveSlave(SelectedSlaveId);
        RefreshSlaveList();
    }

    public void RefreshSlaveList()
    {
        SlaveDevices.Clear();
        if (_slaveService == null) return;
        foreach (var s in _slaveService.GetActiveSlaves())
            SlaveDevices.Add(s);
    }

    private void CreateSlave()
    {
        if (_slaveService == null) return;
        var dialog = new CreateSlaveDialog(_slaveService);
        dialog.Owner = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive);
        if (dialog.ShowDialog() == true)
            RefreshSlaveList();
    }
}
