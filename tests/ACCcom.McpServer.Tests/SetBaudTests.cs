using System.Text.Json;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using ACCcom.McpServer.Tests.TestHelpers;
using ACCcom.McpServer.Tools;
using Xunit;

namespace ACCcom.McpServer.Tests;

/// <summary>set_baud contract: live rate change on an open port, INVALID_CONFIG
/// for non-positive rates, PORT_NOT_OPEN without an open session, and the
/// applied rate (not the argument) echoed back. Uses VirtualSerialService via
/// ToolContextFactory, so the whole path runs without hardware (真机 DTR/RTS
/// 不复位语义待真机验证).</summary>
public class SetBaudTests
{
    [Fact]
    public async Task SetBaud_OpenPort_ChangesRateAndEchoesAppliedValue()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var serial = (VirtualSerialService)ctx.Serial;
            Assert.True(serial.Open(new SerialConfig { PortName = "COM_V", BaudRate = 9600 }));

            var tools = new SerialTools(ctx);
            var result = await tools.SetBaud(115200);

            Assert.True(ToolContextFactory.ExtractSuccess(result));
            using var doc = JsonDocument.Parse(result);
            var data = doc.RootElement.GetProperty("data");
            Assert.Equal(115200, data.GetProperty("baudRate").GetInt32());
            Assert.Equal(115200, data.GetProperty("appliedBaudRate").GetInt32());
            Assert.Equal(115200, serial.BaudRate);
            // The service's tracked config follows, so a later reopen/reconnect
            // does not resurrect 9600.
            Assert.Equal(115200, serial.ActiveConfig!.BaudRate);
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task SetBaud_NonPositiveRate_FailsInvalidConfig()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var serial = (VirtualSerialService)ctx.Serial;
            Assert.True(serial.Open(new SerialConfig { PortName = "COM_V", BaudRate = 9600 }));

            var tools = new SerialTools(ctx);
            var result = await tools.SetBaud(0);

            Assert.False(ToolContextFactory.ExtractSuccess(result));
            Assert.Contains("INVALID_CONFIG", result);
            Assert.Equal(9600, serial.BaudRate); // unchanged
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task SetBaud_PortNotOpen_FailsPortNotOpen()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.SetBaud(115200);

            Assert.False(ToolContextFactory.ExtractSuccess(result));
            Assert.Contains("PORT_NOT_OPEN", result);
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task SetBaud_TaggedSession_RoutesToTagAndUnknownTagFails()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            Assert.True(await ctx.MultiPort.OpenPortAsync("p1",
                new SerialConfig { PortName = "COM_V2", BaudRate = 4800 }));

            var tools = new SerialTools(ctx);
            var ok = await tools.SetBaud(19200, "p1");
            Assert.True(ToolContextFactory.ExtractSuccess(ok));
            Assert.Equal(19200, ctx.MultiPort.GetPort("p1")!.Service.BaudRate);

            var unknown = await tools.SetBaud(19200, "nope");
            Assert.False(ToolContextFactory.ExtractSuccess(unknown));
            Assert.Contains("PORT_NOT_OPEN", unknown);
        }
        finally { sp.Dispose(); }
    }
}
