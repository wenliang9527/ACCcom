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
    public DataBufferService Buffer { get; }
    public event Action<LogEntry>? OnDataEntry;

    public HttpService(ISerialService? serialService = null, ParserManager? parserManager = null, ModbusSlaveService? slaveService = null, string url = DefaultUrl, int bufferCapacity = 10000)
    {
        Buffer = new DataBufferService(bufferCapacity);
        _serialService = serialService;
        _parserManager = parserManager;
        _slaveService = slaveService;
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

    public void Dispose()
    {
        Buffer.CancelWaiters();
        _server?.Dispose();
    }
}

