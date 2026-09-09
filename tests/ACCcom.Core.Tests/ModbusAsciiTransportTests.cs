using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class ModbusAsciiTransportTests
{
    private static byte Lrc(ReadOnlySpan<byte> data) => ModbusAsciiTransport.CalculateLrc(data);

    private static string AsciiFrameHex(byte[] adu)
    {
        var frame = ModbusAsciiTransport.FormatAsciiFrame(adu);
        var bytes = System.Text.Encoding.ASCII.GetBytes(frame);
        return BitConverter.ToString(bytes).Replace("-", " ");
    }

    [Fact]
    public void CalculateLrc_KnownValue()
    {
        var data = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x0A };
        var lrc = ModbusAsciiTransport.CalculateLrc(data);
        Assert.Equal(0xF2, lrc);
    }

    [Fact]
    public void CalculateLrc_SingleByte()
    {
        var lrc = ModbusAsciiTransport.CalculateLrc(new byte[] { 0x01 });
        Assert.Equal(0xFF, lrc);
    }

    [Fact]
    public void CalculateLrc_ZeroSum()
    {
        var data = new byte[] { 0x00, 0x00, 0x00 };
        var lrc = ModbusAsciiTransport.CalculateLrc(data);
        Assert.Equal(0x00, lrc);
    }

    [Fact]
    public void FormatAsciiFrame_ProducesCorrectFormat()
    {
        var adu = new byte[] { 0x01, 0x03, 0x02, 0x00, 0x0A, 0xF2 };
        var frame = ModbusAsciiTransport.FormatAsciiFrame(adu);

        Assert.StartsWith(":", frame);
        Assert.EndsWith("\r\n", frame);
        Assert.Equal(":010302000AF2\r\n", frame);
    }

    [Fact]
    public void FormatAsciiFrame_NoSpacesInHex()
    {
        var adu = new byte[] { 0xAB, 0xCD, 0xEF, 0x12 };
        var frame = ModbusAsciiTransport.FormatAsciiFrame(adu);

        var inner = frame.TrimStart(':').TrimEnd('\r', '\n');
        Assert.DoesNotContain(" ", inner);
    }

    [Fact]
    public void HexStringToBytes_ParsesCorrectly()
    {
        var result = ModbusAsciiTransport.HexStringToBytes("010302000A");
        Assert.Equal(new byte[] { 0x01, 0x03, 0x02, 0x00, 0x0A }, result);
    }

    [Fact]
    public void HexStringToBytes_WithSpaces()
    {
        var result = ModbusAsciiTransport.HexStringToBytes("01 03 02 00 0A");
        Assert.Equal(new byte[] { 0x01, 0x03, 0x02, 0x00, 0x0A }, result);
    }

    [Fact]
    public void Dispose_CleansUpResources()
    {
        using var serial = new VirtualSerialService();
        var transport = new ModbusAsciiTransport(serial);

        transport.Dispose();
        transport.Dispose();
    }

    [Fact]
    public async Task SendReceiveAsync_SendsAsciiFrame()
    {
        using var serial = new VirtualSerialService();
        using var transport = new ModbusAsciiTransport(serial);
        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 9600, DataBits = 7, StopBits = 1, Parity = 0 });

        var requestPdu = new byte[] { 0x00, 0x00, 0x00, 0x0A };

        // Start the request first (it sends the frame synchronously before
        // awaiting), then inject the response — no Task.Delay scheduling race.
        var request = transport.SendReceiveAsync(0x01, 0x03, requestPdu, 1000);
        var respAdu = new byte[] { 0x01, 0x03, 0x04, 0x00, 0x0A, 0x00, 0x64 };
        var respLrc = Lrc(respAdu);
        var respFull = new byte[respAdu.Length + 1];
        Array.Copy(respAdu, respFull, respAdu.Length);
        respFull[^1] = respLrc;
        serial.InjectRxData(AsciiFrameHex(respFull));
        var result = await request;

        Assert.Equal(new byte[] { 0x01, 0x03, 0x04, 0x00, 0x0A, 0x00, 0x64 }, result);
    }

    [Fact]
    public async Task SendReceiveAsync_VerifiesLrc()
    {
        using var serial = new VirtualSerialService();
        using var transport = new ModbusAsciiTransport(serial);
        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 9600, DataBits = 7, StopBits = 1, Parity = 0 });

        var request = transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 1000);
        var respAdu = new byte[] { 0x01, 0x03, 0x02, 0x00, 0x0A };
        var wrongLrc = (byte)(Lrc(respAdu) ^ 0xFF);
        var respFull = new byte[respAdu.Length + 1];
        Array.Copy(respAdu, respFull, respAdu.Length);
        respFull[^1] = wrongLrc;
        serial.InjectRxData(AsciiFrameHex(respFull));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await request);
        Assert.Contains("LRC", ex.Message);
    }

    [Fact]
    public async Task SendReceiveAsync_Timeout_Throws()
    {
        using var serial = new VirtualSerialService();
        using var transport = new ModbusAsciiTransport(serial);
        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 9600, DataBits = 7, StopBits = 1, Parity = 0 });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 100));
    }

    [Fact]
    public async Task SendReceiveAsync_NonPositiveTimeout_ThrowsOperationCanceledNotArgument()
    {
        // A non-positive timeout used to throw ArgumentOutOfRangeException
        // inside CancellationTokenSource; it must behave as an immediate
        // timeout instead (OperationCanceledException).
        using var serial = new VirtualSerialService();
        using var transport = new ModbusAsciiTransport(serial);
        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 9600, DataBits = 7, StopBits = 1, Parity = 0 });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 0));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], -100));
    }

    [Fact]
    public async Task SendReceiveAsync_ExceptionResponse()
    {
        using var serial = new VirtualSerialService();
        using var transport = new ModbusAsciiTransport(serial);
        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 9600, DataBits = 7, StopBits = 1, Parity = 0 });

        var request = transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 1000);
        var respAdu = new byte[] { 0x01, 0x83, 0x02 };
        var respLrc = Lrc(respAdu);
        var respFull = new byte[respAdu.Length + 1];
        Array.Copy(respAdu, respFull, respAdu.Length);
        respFull[^1] = respLrc;
        serial.InjectRxData(AsciiFrameHex(respFull));
        var result = await request;

        Assert.Equal(0x01, result[0]);
        Assert.Equal(0x83, result[1]);
        Assert.Equal(0x02, result[2]);
    }

    [Fact]
    public async Task SendReceiveAsync_FragmentedAsciiMessage()
    {
        using var serial = new VirtualSerialService();
        using var transport = new ModbusAsciiTransport(serial);
        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 9600, DataBits = 7, StopBits = 1, Parity = 0 });

        var respAdu = new byte[] { 0x01, 0x03, 0x02, 0x00, 0x0A };
        var respLrc = Lrc(respAdu);
        var respFull = new byte[respAdu.Length + 1];
        Array.Copy(respAdu, respFull, respAdu.Length);
        respFull[^1] = respLrc;
        var fullAscii = ModbusAsciiTransport.FormatAsciiFrame(respFull);
        var asciiBytes = System.Text.Encoding.ASCII.GetBytes(fullAscii);

        var half = asciiBytes.Length / 2;
        var part1 = BitConverter.ToString(asciiBytes, 0, half).Replace("-", " ");
        var part2 = BitConverter.ToString(asciiBytes, half).Replace("-", " ");

        // Start the request first, then inject the two fragments as consecutive
        // synchronous calls — reassembly across separate RX entries is the point
        // of the test. (A wall-clock gap between the fragments only added a
        // starvation race: under a loaded test run the Delay continuation could
        // land after the 1000ms timeout had already fired.)
        var request = transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 1000);
        serial.InjectRxData(part1);
        serial.InjectRxData(part2);

        var result = await request;

        Assert.Equal(new byte[] { 0x01, 0x03, 0x02, 0x00, 0x0A }, result);
    }

    [Fact]
    public async Task SendReceiveAsync_MultipleSlaveIds_ResolveCorrectly()
    {
        using var serial = new VirtualSerialService();
        using var transport = new ModbusAsciiTransport(serial);
        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 9600, DataBits = 7, StopBits = 1, Parity = 0 });

        // Both requests are issued before either response, so each response
        // lands after its request's frame is on the wire and is routed by
        // slave id + function — deterministic, no Task.Delay race.
        var t1 = transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 1000);
        var t2 = transport.SendReceiveAsync(0x02, 0x03, [0x00, 0x00, 0x00, 0x01], 1000);

        var resp1Adu = new byte[] { 0x01, 0x03, 0x02, 0x00, 0x0A };
        var resp1Full = new byte[resp1Adu.Length + 1];
        Array.Copy(resp1Adu, resp1Full, resp1Adu.Length);
        resp1Full[^1] = Lrc(resp1Adu);
        serial.InjectRxData(AsciiFrameHex(resp1Full));

        var resp2Adu = new byte[] { 0x02, 0x03, 0x02, 0x00, 0x14 };
        var resp2Full = new byte[resp2Adu.Length + 1];
        Array.Copy(resp2Adu, resp2Full, resp2Adu.Length);
        resp2Full[^1] = Lrc(resp2Adu);
        serial.InjectRxData(AsciiFrameHex(resp2Full));

        var results = await Task.WhenAll(t1, t2);
        Assert.Equal(new byte[] { 0x01, 0x03, 0x02, 0x00, 0x0A }, results[0]);
        Assert.Equal(new byte[] { 0x02, 0x03, 0x02, 0x00, 0x14 }, results[1]);
    }

    [Fact]
    public async Task SendReceiveAsync_SendFailure_Throws()
    {
        using var serial = new VirtualSerialService();
        using var transport = new ModbusAsciiTransport(serial);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 1000));
    }

    [Fact]
    public async Task Dispose_FaultsPendingRequests()
    {
        using var serial = new VirtualSerialService();
        using var transport = new ModbusAsciiTransport(serial);
        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 9600, DataBits = 7, StopBits = 1, Parity = 0 });

        var task = transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 5000);
        transport.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => task);
    }

    [Fact]
    public async Task RxBuffer_UnboundedGarbage_IsCappedAndFramesStillParse()
    {
        // A peer that never sends CR/LF would grow the RX buffer without bound;
        // the cap must drop the oldest bytes yet keep the tail so a frame
        // arriving after a flood of CR-free garbage still reassembles.
        using var serial = new VirtualSerialService();
        using var transport = new ModbusAsciiTransport(serial);
        serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 9600, DataBits = 7, StopBits = 1, Parity = 0 });

        // HexStringToBytes halves the character count, so 140K '0's (hex 0x00)
        // become 70KB of CR-free bytes — past the 64KB cap, and nowhere is 0x0D
        // (the CR that would end an ASCII frame).
        var garbage = new string('0', 140 * 1024);
        serial.InjectRxData(garbage);

        // Start a request, then reply with a valid ASCII frame: the trimmed
        // buffer must still find the CR/LF and resolve the pending request.
        var respAdu = new byte[] { 0x01, 0x03, 0x02, 0x00, 0x0A };
        var respFull = new byte[respAdu.Length + 1];
        Array.Copy(respAdu, respFull, respAdu.Length);
        respFull[^1] = Lrc(respAdu);
        var request = transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 1000);
        serial.InjectRxData(AsciiFrameHex(respFull));

        var result = await request;
        Assert.Equal(new byte[] { 0x01, 0x03, 0x02, 0x00, 0x0A }, result);
    }
}
