using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class ModbusSlaveDevice
{
    private readonly bool[] _coils;
    private readonly bool[] _discreteInputs;
    private readonly ushort[] _holdingRegisters;
    private readonly ushort[] _inputRegisters;
    private readonly object _lock = new();

    public byte SlaveId { get; }
    public int CoilCount => _coils.Length;
    public int DiscreteInputCount => _discreteInputs.Length;
    public int HoldingRegisterCount => _holdingRegisters.Length;
    public int InputRegisterCount => _inputRegisters.Length;

    public event Action<ModbusTransaction>? OnTransaction;

    public ModbusSlaveDevice(byte slaveId,
        int coils = 1024, int discreteInputs = 1024,
        int holdingRegisters = 256, int inputRegisters = 256)
    {
        SlaveId = slaveId;
        _coils = new bool[coils];
        _discreteInputs = new bool[discreteInputs];
        _holdingRegisters = new ushort[holdingRegisters];
        _inputRegisters = new ushort[inputRegisters];
    }

    public void SetCoil(ushort addr, bool value) { lock (_lock) { if (addr < _coils.Length) _coils[addr] = value; } }
    public void SetDiscreteInput(ushort addr, bool value) { lock (_lock) { if (addr < _discreteInputs.Length) _discreteInputs[addr] = value; } }
    public void SetHoldingRegister(ushort addr, ushort value) { lock (_lock) { if (addr < _holdingRegisters.Length) _holdingRegisters[addr] = value; } }
    public void SetInputRegister(ushort addr, ushort value) { lock (_lock) { if (addr < _inputRegisters.Length) _inputRegisters[addr] = value; } }

    public bool GetCoil(ushort addr) { lock (_lock) { return addr < _coils.Length && _coils[addr]; } }
    public bool GetDiscreteInput(ushort addr) { lock (_lock) { return addr < _discreteInputs.Length && _discreteInputs[addr]; } }
    public ushort GetHoldingRegister(ushort addr) { lock (_lock) { return addr < _holdingRegisters.Length ? _holdingRegisters[addr] : (ushort)0; } }
    public ushort GetInputRegister(ushort addr) { lock (_lock) { return addr < _inputRegisters.Length ? _inputRegisters[addr] : (ushort)0; } }

    public byte[] HandleRequest(byte functionCode, byte[] pdu)
    {
        try
        {
            return functionCode switch
            {
                0x01 => HandleReadBits(pdu, _coils, 0x01),
                0x02 => HandleReadBits(pdu, _discreteInputs, 0x02),
                0x03 => HandleReadRegisters(pdu, _holdingRegisters, 0x03),
                0x04 => HandleReadRegisters(pdu, _inputRegisters, 0x04),
                0x05 => HandleWriteSingleCoil(pdu),
                0x06 => HandleWriteSingleRegister(pdu),
                0x0F => HandleWriteMultipleCoils(pdu),
                0x10 => HandleWriteMultipleRegisters(pdu),
                0x16 => HandleMaskWriteRegister(pdu),
                0x17 => HandleReadWriteMultipleRegisters(pdu),
                _ => ErrorResponse(functionCode, 0x01)
            };
        }
        catch (IndexOutOfRangeException) { return ErrorResponse(functionCode, 0x02); }
    }

    private byte[] HandleReadBits(byte[] pdu, bool[] bits, byte funcCode)
    {
        var startAddr = (ushort)((pdu[0] << 8) | pdu[1]);
        var count = (ushort)((pdu[2] << 8) | pdu[3]);
        if (count == 0 || count > 2000)
            return ErrorResponse(funcCode, 0x03);
        if (startAddr >= bits.Length)
            return ErrorResponse(funcCode, 0x02);
        if (startAddr + count > bits.Length)
            return ErrorResponse(funcCode, 0x03);
        var byteCount = (count + 7) / 8;
        var data = new byte[1 + byteCount];
        data[0] = (byte)byteCount;
        for (int i = 0; i < count; i++)
            if (bits[startAddr + i]) data[1 + i / 8] |= (byte)(1 << (i % 8));
        NotifyTransaction(funcCode, pdu, data);
        return data;
    }

    private byte[] HandleReadRegisters(byte[] pdu, ushort[] regs, byte funcCode)
    {
        var startAddr = (ushort)((pdu[0] << 8) | pdu[1]);
        var count = (ushort)((pdu[2] << 8) | pdu[3]);
        if (count == 0 || count > 125 || startAddr + count > regs.Length)
            return ErrorResponse(funcCode, count == 0 ? (byte)0x03 : (byte)0x02);
        var data = new byte[1 + count * 2];
        data[0] = (byte)(count * 2);
        for (int i = 0; i < count; i++)
        { data[1 + i * 2] = (byte)(regs[startAddr + i] >> 8); data[1 + i * 2 + 1] = (byte)regs[startAddr + i]; }
        NotifyTransaction(funcCode, pdu, data);
        return data;
    }

    private byte[] HandleWriteSingleCoil(byte[] pdu)
    {
        var addr = (ushort)((pdu[0] << 8) | pdu[1]);
        var value = (ushort)((pdu[2] << 8) | pdu[3]);
        if (addr >= _coils.Length) return ErrorResponse(0x05, 0x02);
        if (value != 0xFF00 && value != 0x0000) return ErrorResponse(0x05, 0x03);
        _coils[addr] = value == 0xFF00;
        NotifyTransaction(0x05, pdu, pdu);
        return pdu;
    }

    private byte[] HandleWriteSingleRegister(byte[] pdu)
    {
        var addr = (ushort)((pdu[0] << 8) | pdu[1]);
        var value = (ushort)((pdu[2] << 8) | pdu[3]);
        if (addr >= _holdingRegisters.Length) return ErrorResponse(0x06, 0x02);
        _holdingRegisters[addr] = value;
        NotifyTransaction(0x06, pdu, pdu);
        return pdu;
    }

    private byte[] HandleWriteMultipleCoils(byte[] pdu)
    {
        var startAddr = (ushort)((pdu[0] << 8) | pdu[1]);
        var count = (ushort)((pdu[2] << 8) | pdu[3]);
        if (startAddr + count > _coils.Length) return ErrorResponse(0x0F, 0x02);
        for (int i = 0; i < count; i++)
            _coils[startAddr + i] = ((pdu[5 + i / 8] >> (i % 8)) & 1) == 1;
        var resp = new byte[] { pdu[0], pdu[1], pdu[2], pdu[3] };
        NotifyTransaction(0x0F, pdu, resp);
        return resp;
    }

    private byte[] HandleWriteMultipleRegisters(byte[] pdu)
    {
        var startAddr = (ushort)((pdu[0] << 8) | pdu[1]);
        var count = (ushort)((pdu[2] << 8) | pdu[3]);
        if (startAddr + count > _holdingRegisters.Length) return ErrorResponse(0x10, 0x02);
        for (int i = 0; i < count; i++)
            _holdingRegisters[startAddr + i] = (ushort)((pdu[5 + i * 2] << 8) | pdu[5 + i * 2 + 1]);
        var resp = new byte[] { pdu[0], pdu[1], pdu[2], pdu[3] };
        NotifyTransaction(0x10, pdu, resp);
        return resp;
    }

    private byte[] HandleMaskWriteRegister(byte[] pdu)
    {
        var addr = (ushort)((pdu[0] << 8) | pdu[1]);
        var andMask = (ushort)((pdu[2] << 8) | pdu[3]);
        var orMask = (ushort)((pdu[4] << 8) | pdu[5]);
        if (addr >= _holdingRegisters.Length) return ErrorResponse(0x16, 0x02);
        _holdingRegisters[addr] = (ushort)((_holdingRegisters[addr] & andMask) | orMask);
        NotifyTransaction(0x16, pdu, pdu);
        return pdu;
    }

    private byte[] HandleReadWriteMultipleRegisters(byte[] pdu)
    {
        var readAddr = (ushort)((pdu[0] << 8) | pdu[1]);
        var readCount = (ushort)((pdu[2] << 8) | pdu[3]);
        var writeAddr = (ushort)((pdu[4] << 8) | pdu[5]);
        var writeCount = (ushort)((pdu[6] << 8) | pdu[7]);
        if (readAddr + readCount > _holdingRegisters.Length || writeAddr + writeCount > _holdingRegisters.Length)
            return ErrorResponse(0x17, 0x02);
        for (int i = 0; i < writeCount; i++)
            _holdingRegisters[writeAddr + i] = (ushort)((pdu[9 + i * 2] << 8) | pdu[9 + i * 2 + 1]);
        var resp = new byte[1 + readCount * 2];
        resp[0] = (byte)(readCount * 2);
        for (int i = 0; i < readCount; i++)
        { resp[1 + i * 2] = (byte)(_holdingRegisters[readAddr + i] >> 8); resp[1 + i * 2 + 1] = (byte)_holdingRegisters[readAddr + i]; }
        NotifyTransaction(0x17, pdu, resp);
        return resp;
    }

    private static byte[] ErrorResponse(byte funcCode, byte exceptionCode)
        => [(byte)(0x80 | funcCode), exceptionCode];

    private void NotifyTransaction(byte funcCode, byte[] reqPdu, byte[] respPdu)
    {
        OnTransaction?.Invoke(new ModbusTransaction
        {
            Timestamp = DateTime.Now,
            SlaveId = SlaveId,
            FunctionCode = (ModbusFunctionCode)funcCode,
            RequestHex = HexHelper.BytesToHexSpaced(reqPdu, 0, reqPdu.Length),
            ResponseHex = HexHelper.BytesToHexSpaced(respPdu, 0, respPdu.Length),
            IsSuccess = true,
            Response = new ModbusResponse { SlaveId = SlaveId, FunctionCode = (ModbusFunctionCode)funcCode, Data = respPdu, IsError = false }
        });
    }
}
