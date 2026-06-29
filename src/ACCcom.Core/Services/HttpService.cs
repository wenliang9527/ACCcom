using EmbedIO;
using EmbedIO.WebApi;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class HttpService : IDisposable
{
    public const string DefaultUrl = "http://127.0.0.1:8899";

    private readonly WebServer _server;
    private readonly ISerialService? _serialService;
    private readonly ParserManager? _parserManager;
    private readonly ModbusSlaveService? _slaveService;
    private readonly MultiPortService? _multiPort;
    private readonly ModbusService? _modbus;
    private readonly ModbusConnectionManager? _modbusConnections;
    private readonly AutoBaudDetector? _autoBaud;
    private readonly SessionRecorder? _recorder;
    private readonly DataStatistics? _stats;
    public DataBufferService Buffer { get; }
    public event Action<LogEntry>? OnDataEntry;

    public HttpService(ISerialService? serialService = null, ParserManager? parserManager = null, ModbusSlaveService? slaveService = null,
        MultiPortService? multiPort = null, ModbusService? modbus = null, ModbusConnectionManager? modbusConnections = null,
        AutoBaudDetector? autoBaud = null, SessionRecorder? recorder = null, DataStatistics? stats = null,
        string url = DefaultUrl, int bufferCapacity = 10000)
    {
        Buffer = new DataBufferService(bufferCapacity);
        _serialService = serialService;
        _parserManager = parserManager;
        _slaveService = slaveService;
        _multiPort = multiPort;
        _modbus = modbus;
        _modbusConnections = modbusConnections;
        _autoBaud = autoBaud;
        _recorder = recorder;
        _stats = stats;
        _server = new WebServer(o => o.WithUrlPrefix(url).WithMode(HttpListenerMode.EmbedIO))
            .WithWebApi("/api", m => m.WithController(() => new SerialController(this)))
            .WithModule(new SerialWebSocketHandler("/ws", this));
        var asmDir = Path.GetDirectoryName(typeof(HttpService).Assembly.Location)!;
        var wwwroot = Path.Combine(asmDir, "wwwroot");
        if (Directory.Exists(wwwroot))
            _server = _server.WithStaticFolder("/dashboard", wwwroot, true);
    }

    public void Start() => _server.Start();
    public void Stop() => _server.Dispose();

    public void AddEntry(LogEntry entry)
    {
        Buffer.AddEntry(entry);
        OnDataEntry?.Invoke(entry);
    }

    public List<LogEntry> GetEntriesSince(int id) => Buffer.GetEntriesSince(id);

    public void ClearBuffer(string? target) => Buffer.Clear(target);

    public bool SendToSerial(string data, bool isHex)
    {
        return _serialService?.Send(data, isHex) ?? false;
    }

    public bool OpenPort(OpenPortRequest req)
    {
        if (_serialService == null) return false;
        if (_serialService.IsOpen) return true;

        var config = new SerialConfig
        {
            PortName = req.Port,
            BaudRate = req.BaudRate,
            DataBits = req.DataBits,
            StopBits = req.StopBits,
            Parity = req.Parity,
            DtrEnable = req.Dtr,
            RtsEnable = req.Rts
        };
        return _serialService.Open(config);
    }

    public bool ClosePort()
    {
        return _serialService?.Close() ?? true;
    }

    public object GetStatus()
    {
        return new
        {
            isOpen = _serialService?.IsOpen ?? false,
            currentPort = _serialService?.CurrentPort,
            baudRate = _serialService?.BaudRate ?? 0,
            rxCount = GetRxCount(),
            txCount = GetTxCount(),
            bufferCount = GetBufferCount()
        };
    }

    private int GetRxCount() => Buffer.CountWhere(e => e.Direction == "RX");

    private int GetTxCount() => Buffer.CountWhere(e => e.Direction == "TX");

    private int GetBufferCount() => Buffer.Count();

    public List<string> GetAvailableParsers()
    {
        return _parserManager?.AvailableParsers.ToList() ?? new List<string>();
    }

    public bool ActivateParser(string? name)
    {
        return _parserManager?.Activate(name) ?? false;
    }

    public string? GetActiveParser()
    {
        return _parserManager?.ActiveParserName;
    }

    public string? GetParserError()
    {
        return _parserManager?.LastError;
    }

    public string? GetParserDir()
    {
        return _parserManager?.GetParserDir();
    }

    public string? ReadParserCode(string name)
    {
        var dir = _parserManager?.GetParserDir();
        if (dir == null) return null;
        var path = Path.Combine(dir, name + ".csx");
        if (!File.Exists(path)) return null;
        return File.ReadAllText(path);
    }

    public bool WriteParserCode(string name, string code)
    {
        var dir = _parserManager?.GetParserDir();
        if (dir == null) return false;
        var path = Path.Combine(dir, name + ".csx");
        File.WriteAllText(path, code);
        _parserManager?.Refresh();
        return true;
    }

    public async Task<(List<FieldAnnotation>? fields, string? error)> ParseRawHexAsync(string hex, string? parserName = null)
    {
        if (_parserManager == null) return (null, "ParserManager not available");

        byte[] data;
        try { data = Convert.FromHexString(hex.Replace(" ", "")); }
        catch { return (null, "Invalid hex string"); }

        var engine = _parserManager.Engine;
        ParserEngine? tempEngine = null;
        try
        {
            if (!string.IsNullOrEmpty(parserName) && parserName != _parserManager.ActiveParserName)
            {
                tempEngine = new ParserEngine();
                var dir = _parserManager.GetParserDir();
                var path = Path.Combine(dir, parserName + ".csx");
                if (!File.Exists(path)) return (null, $"Parser '{parserName}' not found");
                if (!tempEngine.Load(File.ReadAllText(path))) return (null, $"Parser load failed: {tempEngine.LastError}");
                engine = tempEngine;
            }
            if (_parserManager.ActiveParserName == null && string.IsNullOrEmpty(parserName))
                return (null, "No active parser. Activate one first or specify parserName.");

            var fields = await engine.ExecuteAsync(data, DateTime.Now).ConfigureAwait(false);
            return (fields, null);
        }
        finally
        {
            tempEngine?.Dispose();
        }
    }

    public object GetSlaves()
    {
        if (_slaveService == null) return new { slaves = Array.Empty<object>(), registers = new object() };
        var slaves = _slaveService.GetActiveSlaves().ToList();
        var regs = new Dictionary<string, List<object>>();
        foreach (var s in slaves)
        {
            var device = _slaveService.GetDevice(s.Id);
            if (device == null) continue;
            var list = new List<object>();
            var count = Math.Min(device.HoldingRegisterCount, 32);
            for (int i = 0; i < count; i++)
            {
                var v = device.GetHoldingRegister((ushort)i);
                list.Add(new { address = i, value = (int)v, hex = $"0x{v:X4}" });
            }
            regs[s.Id] = list;
        }
        return new { slaves, registers = regs };
    }

    // ========== Multi-Port ==========

    public bool MultiPortOpen(MultiPortOpenRequest req)
    {
        if (_multiPort == null) return false;
        var config = new SerialConfig
        {
            PortName = req.Port, BaudRate = req.BaudRate, DataBits = req.DataBits,
            StopBits = req.StopBits, Parity = req.Parity, DtrEnable = req.Dtr, RtsEnable = req.Rts
        };
        return _multiPort.OpenPort(req.Tag, config);
    }

    public bool MultiPortClose(string tag) => _multiPort?.ClosePort(tag) ?? false;

    public bool MultiPortSend(string tag, string data, bool isHex) => _multiPort?.SendToPort(tag, data, isHex) ?? false;

    // ========== Modbus ==========

    public ModbusService? GetModbusService(string? connectionId)
    {
        if (_modbus == null) return null;
        if (!string.IsNullOrEmpty(connectionId) && connectionId != "default")
            return _modbusConnections?.GetService(connectionId);
        return _modbus;
    }

    public async Task<ModbusResponse> ReadRegistersAsync(ModbusReadRequest req)
    {
        var svc = GetModbusService(req.ConnectionId);
        if (svc == null) return new ModbusResponse { IsError = true, ErrorMessage = "MODBUS service not available" };

        return req.FunctionCode switch
        {
            "ReadCoils" or "01" => await svc.ReadCoilsAsync(req.SlaveId, req.StartAddress, req.Quantity, req.TimeoutMs).ConfigureAwait(false),
            "ReadDiscreteInputs" or "02" => await svc.ReadDiscreteInputsAsync(req.SlaveId, req.StartAddress, req.Quantity, req.TimeoutMs).ConfigureAwait(false),
            "ReadHoldingRegisters" or "03" => await svc.ReadHoldingRegistersAsync(req.SlaveId, req.StartAddress, req.Quantity, req.TimeoutMs).ConfigureAwait(false),
            "ReadInputRegisters" or "04" => await svc.ReadInputRegistersAsync(req.SlaveId, req.StartAddress, req.Quantity, req.TimeoutMs).ConfigureAwait(false),
            _ => await svc.ReadHoldingRegistersAsync(req.SlaveId, req.StartAddress, req.Quantity, req.TimeoutMs).ConfigureAwait(false)
        };
    }

    public async Task<ModbusResponse> WriteRegisterAsync(ModbusWriteRequest req)
    {
        var svc = GetModbusService(req.ConnectionId);
        if (svc == null) return new ModbusResponse { IsError = true, ErrorMessage = "MODBUS service not available" };

        return req.FunctionCode switch
        {
            "WriteSingleCoil" or "05" => await svc.WriteSingleCoilAsync(req.SlaveId, req.Address, req.Value != 0, req.TimeoutMs).ConfigureAwait(false),
            "WriteSingleRegister" or "06" => await svc.WriteSingleRegisterAsync(req.SlaveId, req.Address, req.Value, req.TimeoutMs).ConfigureAwait(false),
            "WriteMultipleCoils" or "15" => await svc.WriteMultipleCoilsAsync(req.SlaveId, req.Address, ParseCoils(req.Values ?? ""), req.TimeoutMs).ConfigureAwait(false),
            "WriteMultipleRegisters" or "16" => await svc.WriteMultipleRegistersAsync(req.SlaveId, req.Address, ParseRegisters(req.Values ?? ""), req.TimeoutMs).ConfigureAwait(false),
            "MaskWriteRegister" or "22" => await svc.MaskWriteRegisterAsync(req.SlaveId, req.Address, ParseHexOrZero(req.AndMask), ParseHexOrZero(req.OrMask), req.TimeoutMs).ConfigureAwait(false),
            "ReadWriteMultipleRegisters" or "23" => await svc.ReadWriteMultipleRegistersAsync(req.SlaveId, req.Address, req.Value, req.Address, ParseRegisters(req.Values ?? ""), req.TimeoutMs).ConfigureAwait(false),
            _ => await svc.WriteSingleRegisterAsync(req.SlaveId, req.Address, req.Value, req.TimeoutMs).ConfigureAwait(false)
        };
    }

    public async Task<List<object>> ScanModbusDevicesAsync(ModbusScanRequest req)
    {
        var svc = GetModbusService(req.ConnectionId);
        if (svc == null) return new List<object>();
        var scanner = new ModbusScanner(svc);
        var devices = await scanner.ScanAsync(req.StartAddress, req.EndAddress, req.TimeoutMs).ConfigureAwait(false);
        return devices.Select(d => (object)new
        {
            slaveId = (int)d.SlaveId,
            slaveIdHex = $"0x{d.SlaveId:X2}",
            isOnline = d.IsOnline,
            firstRegisterValue = $"0x{d.FirstRegisterValue:X4}"
        }).ToList();
    }

    public string? SlaveCreate(SlaveCreateRequest req)
    {
        if (_slaveService == null) return null;
        return _slaveService.CreateSlave(req.SlaveId, req.Transport, req.ConnectionParam, req.Coils, req.DiscreteInputs, req.HoldingRegisters, req.InputRegisters);
    }

    public bool SlaveRemove(string slaveId)
    {
        if (_slaveService == null) return false;
        _slaveService.RemoveSlave(slaveId);
        return true;
    }

    public List<SlaveInfo>? SlaveList() => _slaveService?.GetActiveSlaves().ToList();

    public bool SlaveWrite(SlaveWriteRequest req)
    {
        if (_slaveService == null) return false;
        var rt = MapRegisterType(req.Type);
        _slaveService.WriteRegister(req.SlaveId, rt, req.Address, req.Value);
        return true;
    }

    public (ushort value, bool ok) SlaveRead(SlaveReadRequest req)
    {
        if (_slaveService == null) return (0, false);
        var rt = MapRegisterType(req.Type);
        return (_slaveService.ReadRegister(req.SlaveId, rt, req.Address), true);
    }

    // ========== Auto Baud ==========

    public async Task<int> DetectBaudRateAsync(string port)
    {
        if (_autoBaud == null) return -1;
        return await _autoBaud.DetectAsync(port).ConfigureAwait(false);
    }

    // ========== Statistics ==========

    public object? GetStatistics()
    {
        if (_stats == null) return null;
        return new
        {
            rxBytesPerSec = Math.Round(_stats.RxBytesPerSecond, 1),
            rxFramesPerSec = Math.Round(_stats.RxFramesPerSecond, 1),
            errorRatePercent = Math.Round(_stats.ErrorRate, 2),
            avgFrameIntervalMs = Math.Round(_stats.AvgFrameIntervalMs, 2),
            totalRxBytes = _stats.TotalRxBytes,
            totalRxFrames = _stats.TotalRxFrames,
            totalErrorFrames = _stats.TotalErrorFrames
        };
    }

    // ========== Recording ==========

    public (bool ok, string? file) RecordingStart(string? filename)
    {
        if (_recorder == null) return (false, null);
        if (_recorder.StartRecording(filename))
            return (true, _recorder.CurrentFile);
        return (false, _recorder.CurrentFile);
    }

    public (bool ok, string? file, int count) RecordingStop()
    {
        if (_recorder == null) return (false, null, 0);
        var file = _recorder.CurrentFile;
        var count = _recorder.RecordedCount;
        var ok = _recorder.StopRecording();
        return (ok, file, count);
    }

    public List<LogEntry> RecordingReplay(string filename)
    {
        if (_recorder == null) return new List<LogEntry>();
        return _recorder.ReplayFile(filename);
    }

    public (bool isRecording, string? file, int count) GetRecordingStatus()
    {
        if (_recorder == null) return (false, null, 0);
        return (_recorder.IsRecording, _recorder.CurrentFile, _recorder.RecordedCount);
    }

    // ========== Helpers ==========

    private static RegisterType MapRegisterType(string type) => type.ToLowerInvariant() switch
    {
        "coil" => RegisterType.Coil,
        "holding" => RegisterType.HoldingRegister,
        "discrete" => RegisterType.DiscreteInput,
        "input" => RegisterType.InputRegister,
        _ => RegisterType.HoldingRegister
    };

    private static bool[] ParseCoils(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return [];
        return input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s is "1" or "true" or "on" or "yes").ToArray();
    }

    private static ushort ParseHexOrZero(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return 0;
        var s = input.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ushort.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : (ushort)0;
        return ushort.TryParse(s, out var dv) ? dv : (ushort)0;
    }

    private static ushort[] ParseRegisters(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return [];
        return input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? ushort.Parse(s[2..], System.Globalization.NumberStyles.HexNumber)
                : ushort.TryParse(s, out var v) ? v : (ushort)0)
            .ToArray();
    }

    public void Dispose()
    {
        Buffer.Dispose();
        _server?.Dispose();
    }
}

