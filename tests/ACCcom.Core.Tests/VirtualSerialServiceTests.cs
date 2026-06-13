using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class VirtualSerialServiceTests
{
    [Fact]
    public void Open_Sets_IsOpen_True()
    {
        var svc = new VirtualSerialService();
        var config = new SerialConfig { PortName = "VIRTUAL", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 };
        var result = svc.Open(config);
        Assert.True(result);
        Assert.True(svc.IsOpen);
        Assert.Equal("VIRTUAL", svc.CurrentPort);
        Assert.Equal(115200, svc.BaudRate);
    }

    [Fact]
    public void Close_Sets_IsOpen_False()
    {
        var svc = new VirtualSerialService();
        svc.Open(new SerialConfig { PortName = "VIRTUAL", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
        var result = svc.Close();
        Assert.True(result);
        Assert.False(svc.IsOpen);
    }

    [Fact]
    public void Send_Without_Open_Returns_False()
    {
        var svc = new VirtualSerialService();
        var result = svc.Send("test");
        Assert.False(result);
    }

    [Fact]
    public void Send_Stores_Entry_In_SentData()
    {
        var svc = new VirtualSerialService();
        svc.Open(new SerialConfig { PortName = "VIRTUAL", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
        var result = svc.Send("Hello");
        Assert.True(result);
        var sent = svc.GetSentData();
        Assert.Single(sent);
        Assert.Equal("TX", sent[0].Direction);
        Assert.Equal("Hello", sent[0].Text);
    }

    [Fact]
    public void SendHex_Stores_Entry()
    {
        var svc = new VirtualSerialService();
        svc.Open(new SerialConfig { PortName = "VIRTUAL", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
        svc.SendHex("AA55");
        var sent = svc.GetSentData();
        Assert.Single(sent);
        Assert.Contains("AA55", sent[0].RawHex, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InjectRxData_Fires_OnDataReceived()
    {
        var svc = new VirtualSerialService();
        svc.Open(new SerialConfig { PortName = "VIRTUAL", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
        LogEntry? received = null;
        svc.OnDataReceived += e => received = e;
        svc.InjectRxData("AA 55 03 01 19");
        Assert.NotNull(received);
        Assert.Equal("RX", received!.Direction);
        Assert.Contains("AA 55", received.RawHex, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InjectRxData_Without_Open_Does_Not_Throw()
    {
        var svc = new VirtualSerialService();
        try
        {
            svc.InjectRxData("AA 55");
        }
        catch (Exception ex)
        {
            Assert.Fail($"Unexpected exception: {ex.Message}");
        }
    }

    [Fact]
    public void Close_Disconnects_Without_Error()
    {
        var svc = new VirtualSerialService();
        svc.Open(new SerialConfig { PortName = "VIRTUAL", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
        svc.Close();
        Assert.False(svc.IsOpen);
    }
}
