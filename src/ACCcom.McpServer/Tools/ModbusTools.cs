using System.ComponentModel;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using ModelContextProtocol.Server;

namespace ACCcom.McpServer.Tools;

[McpServerToolType]
public class ModbusTools
{
    private readonly ToolContext _ctx;

    public ModbusTools(ToolContext ctx)
    {
        _ctx = ctx;
    }

    private ModbusService? GetModbus(string? connectionId)
    {
        if (_ctx.UseProxy)
            return null;
        if (!string.IsNullOrEmpty(connectionId) && connectionId != "default")
            return _ctx.ConnectionManager.GetService(connectionId);
        return _ctx.Modbus;
    }

    [McpServerTool, Description("Read MODBUS registers from a slave device. Supports function codes: ReadCoils (01), ReadDiscreteInputs (02), ReadHoldingRegisters (03), ReadInputRegisters (04). Parameters: slaveId (default 1), functionCode (default ReadHoldingRegisters), startAddress (default 0), quantity (default 10), timeoutMs (default 1000), connectionId (optional, defaults to 'default').")]
    public async Task<string> ReadRegisters(
        byte slaveId = 1,
        string functionCode = "ReadHoldingRegisters",
        ushort startAddress = 0,
        ushort quantity = 10,
        int timeoutMs = 1000,
        string? connectionId = null)
    {
        if (_ctx.UseProxy)
            return await _ctx.Proxy!.PostAsync("/api/modbus/read", new { slaveId, functionCode, startAddress, quantity, timeoutMs, connectionId }).ConfigureAwait(false);

        var modbus = GetModbus(connectionId);
        if (modbus == null)
            return _ctx.RawJson(new { success = false, error = "MODBUS service not available" });

        ModbusResponse result;
        try
        {
            result = functionCode switch
            {
                "ReadCoils" or "01" => await modbus.ReadCoilsAsync(slaveId, startAddress, quantity, timeoutMs).ConfigureAwait(false),
                "ReadDiscreteInputs" or "02" => await modbus.ReadDiscreteInputsAsync(slaveId, startAddress, quantity, timeoutMs).ConfigureAwait(false),
                "ReadHoldingRegisters" or "03" => await modbus.ReadHoldingRegistersAsync(slaveId, startAddress, quantity, timeoutMs).ConfigureAwait(false),
                "ReadInputRegisters" or "04" => await modbus.ReadInputRegistersAsync(slaveId, startAddress, quantity, timeoutMs).ConfigureAwait(false),
                _ => await modbus.ReadHoldingRegistersAsync(slaveId, startAddress, quantity, timeoutMs).ConfigureAwait(false)
            };
        }
        catch (Exception ex)
        {
            return _ctx.RawJson(new { success = false, error = $"MODBUS error: {ex.Message}" });
        }

        if (result.IsError)
            return _ctx.RawJson(new { success = false, error = result.ErrorMessage });

        var registers = new List<object>();
        for (int i = 0; i + 1 < result.Data.Length && registers.Count < quantity; i += 2)
        {
            var val = (ushort)((result.Data[i] << 8) | result.Data[i + 1]);
            registers.Add(new { address = (ushort)(startAddress + registers.Count), value = val, hex = $"0x{val:X4}" });
        }

        return _ctx.RawJson(new
        {
            success = true,
            data = new
            {
                slaveId = (int)result.SlaveId,
                functionCode = result.FunctionCode.ToString(),
                registerCount = registers.Count,
                registers
            }
        });
    }

    [McpServerTool, Description("Write MODBUS registers or coils. Supports: WriteSingleCoil (05), WriteSingleRegister (06), WriteMultipleCoils (15), WriteMultipleRegisters (16), MaskWriteRegister (22), ReadWriteMultipleRegisters (23). For batch writes, pass values as comma-separated: values='1,0,1,0' or values='0x0A,0x0B'. Parameters: slaveId (default 1), functionCode (default WriteSingleRegister), address (default 0), value (default 0), values (optional for batch writes), andMask (for FC22), orMask (for FC22), timeoutMs (default 1000), connectionId (optional).")]
    public async Task<string> WriteRegister(
        byte slaveId = 1,
        string functionCode = "WriteSingleRegister",
        ushort address = 0,
        ushort value = 0,
        string? values = null,
        string? andMask = null,
        string? orMask = null,
        int timeoutMs = 1000,
        string? connectionId = null)
    {
        if (_ctx.UseProxy)
            return await _ctx.Proxy!.PostAsync("/api/modbus/write", new { slaveId, functionCode, address, value, values, andMask, orMask, timeoutMs, connectionId }).ConfigureAwait(false);

        var modbus = GetModbus(connectionId);
        if (modbus == null)
            return _ctx.RawJson(new { success = false, error = "MODBUS service not available" });

        ModbusResponse result;
        try
        {
            result = functionCode switch
            {
                "WriteSingleCoil" or "05" => await modbus.WriteSingleCoilAsync(slaveId, address, value != 0, timeoutMs).ConfigureAwait(false),
                "WriteSingleRegister" or "06" => await modbus.WriteSingleRegisterAsync(slaveId, address, value, timeoutMs).ConfigureAwait(false),
                "WriteMultipleCoils" or "15" => await modbus.WriteMultipleCoilsAsync(slaveId, address, ParseCoils(values ?? ""), timeoutMs).ConfigureAwait(false),
                "WriteMultipleRegisters" or "16" => await modbus.WriteMultipleRegistersAsync(slaveId, address, ParseRegisters(values ?? ""), timeoutMs).ConfigureAwait(false),
                "MaskWriteRegister" or "22" => await modbus.MaskWriteRegisterAsync(slaveId, address, ParseHexOrZero(andMask), ParseHexOrZero(orMask), timeoutMs).ConfigureAwait(false),
                "ReadWriteMultipleRegisters" or "23" => await modbus.ReadWriteMultipleRegistersAsync(slaveId, address, value, address, ParseRegisters(values ?? ""), timeoutMs).ConfigureAwait(false),
                _ => await modbus.WriteSingleRegisterAsync(slaveId, address, value, timeoutMs).ConfigureAwait(false)
            };
        }
        catch (Exception ex)
        {
            return _ctx.RawJson(new { success = false, error = $"MODBUS error: {ex.Message}" });
        }

        if (result.IsError)
            return _ctx.RawJson(new { success = false, error = result.ErrorMessage });

        return _ctx.RawJson(new
        {
            success = true,
            data = new
            {
                slaveId = (int)result.SlaveId,
                functionCode = result.FunctionCode.ToString(),
                address,
                value,
                hex = $"0x{value:X4}"
            }
        });
    }

    [McpServerTool, Description("Create a virtual MODBUS slave device. Parameters: slaveId (default 1), transport ('rtu' or 'tcp', default 'tcp'), connectionParam (COM port for rtu, port number for tcp, default '15000'), coils (default 1024), discreteInputs (default 1024), holdingRegisters (default 256), inputRegisters (default 256).")]
    public async Task<string> SlaveCreate(byte slaveId = 1, string transport = "tcp", string connectionParam = "15000",
        int coils = 1024, int discreteInputs = 1024, int holdingRegisters = 256, int inputRegisters = 256)
    {
        if (_ctx.UseProxy)
            return await _ctx.Proxy!.PostAsync("/api/slave/create", new { slaveId, transport, connectionParam, coils, discreteInputs, holdingRegisters, inputRegisters }).ConfigureAwait(false);
        var svc = _ctx.SlaveService;
        if (svc == null) return _ctx.RawJson(new { success = false, error = "SlaveService not available" });
        var id = svc.CreateSlave(slaveId, transport, connectionParam, coils, discreteInputs, holdingRegisters, inputRegisters);
        return _ctx.RawJson(new { success = true, data = new { id, slaveId, transport, connectionParam } });
    }

    [McpServerTool, Description("Remove a MODBUS slave device. Parameters: slaveId (the id returned by slave_create).")]
    public async Task<string> SlaveRemove(string slaveId)
    {
        if (_ctx.UseProxy)
            return await _ctx.Proxy!.PostAsync("/api/slave/remove", new { tag = slaveId }).ConfigureAwait(false);
        var svc = _ctx.SlaveService;
        if (svc == null) return _ctx.RawJson(new { success = false, error = "SlaveService not available" });
        svc.RemoveSlave(slaveId);
        return _ctx.RawJson(new { success = true, data = new { slaveId, removed = true } });
    }

    [McpServerTool, Description("List all active MODBUS slave devices.")]
    public async Task<string> SlaveList()
    {
        if (_ctx.UseProxy)
            return await _ctx.Proxy!.GetAsync("/api/slave/list").ConfigureAwait(false);
        var svc = _ctx.SlaveService;
        if (svc == null) return _ctx.RawJson(new { success = false, error = "SlaveService not available" });
        var slaves = svc.GetActiveSlaves().ToList();
        return _ctx.RawJson(new { success = true, data = new { count = slaves.Count, slaves } });
    }

    [McpServerTool, Description("Write a register value on a MODBUS slave device. Parameters: slaveId, type ('coil'/'holding'/'discrete'/'input'), address, value.")]
    public async Task<string> SlaveWrite(string slaveId, string type, ushort address, ushort value)
    {
        if (_ctx.UseProxy)
            return await _ctx.Proxy!.PostAsync("/api/slave/write", new { slaveId, type, address, value }).ConfigureAwait(false);
        var svc = _ctx.SlaveService;
        if (svc == null) return _ctx.RawJson(new { success = false, error = "SlaveService not available" });
        var rt = type.ToLowerInvariant() switch
        { "coil" => RegisterType.Coil, "holding" => RegisterType.HoldingRegister, "discrete" => RegisterType.DiscreteInput, "input" => RegisterType.InputRegister, _ => RegisterType.HoldingRegister };
        svc.WriteRegister(slaveId, rt, address, value);
        return _ctx.RawJson(new { success = true, data = new { slaveId, type, address, value } });
    }

    [McpServerTool, Description("Read a register value from a MODBUS slave device. Parameters: slaveId, type, address.")]
    public async Task<string> SlaveRead(string slaveId, string type, ushort address)
    {
        if (_ctx.UseProxy)
            return await _ctx.Proxy!.PostAsync("/api/slave/read", new { slaveId, type, address }).ConfigureAwait(false);
        var svc = _ctx.SlaveService;
        if (svc == null) return _ctx.RawJson(new { success = false, error = "SlaveService not available" });
        var rt = type.ToLowerInvariant() switch
        { "coil" => RegisterType.Coil, "holding" => RegisterType.HoldingRegister, "discrete" => RegisterType.DiscreteInput, "input" => RegisterType.InputRegister, _ => RegisterType.HoldingRegister };
        var value = svc.ReadRegister(slaveId, rt, address);
        return _ctx.RawJson(new { success = true, data = new { slaveId, type, address, value } });
    }

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

    [McpServerTool, Description("Scan for MODBUS slave devices on the bus. Parameters: startAddress (default 1), endAddress (default 247), timeoutMs (default 500), connectionId (optional). Returns list of online devices.")]
    public async Task<string> ScanDevices(
        byte startAddress = 1,
        byte endAddress = 247,
        int timeoutMs = 500,
        string? connectionId = null)
    {
        if (_ctx.UseProxy)
            return await _ctx.Proxy!.PostAsync("/api/modbus/scan", new { startAddress, endAddress, timeoutMs, connectionId }).ConfigureAwait(false);

        var modbus = GetModbus(connectionId);
        if (modbus == null)
            return _ctx.RawJson(new { success = false, error = "MODBUS service not available" });

        var scanner = new ModbusScanner(modbus);
        try
        {
            var devices = await scanner.ScanAsync(startAddress, endAddress, timeoutMs).ConfigureAwait(false);

            return _ctx.RawJson(new
            {
                success = true,
                data = new
                {
                    scannedRange = $"{startAddress}-{endAddress}",
                    timeoutMs,
                    deviceCount = devices.Count,
                    devices = devices.Select(d => new
                    {
                        slaveId = (int)d.SlaveId,
                        slaveIdHex = $"0x{d.SlaveId:X2}",
                        isOnline = d.IsOnline,
                        firstRegisterValue = $"0x{d.FirstRegisterValue:X4}"
                    })
                }
            });
        }
        catch (OperationCanceledException)
        {
            return _ctx.RawJson(new { success = false, error = "Scan cancelled" });
        }
    }
}
