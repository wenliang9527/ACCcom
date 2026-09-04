using ACCcom.McpServer.Tests.TestHelpers;
using ACCcom.McpServer.Tools;

namespace ACCcom.McpServer.Tests;

public class SerialToolsTests
{
    [Fact]
    public async Task ListPorts_ReturnsSuccessJson()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.ListPorts();
            Assert.True(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task OpenPort_RequiresPortName()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.OpenPort("");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
            Assert.Contains("required", ToolContextFactory.ExtractError(result) ?? "");
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task Send_RejectsEmptyData()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.Send("");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ClearBuffer_ReturnsSuccess()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.ClearBuffer("all");
            Assert.True(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReadData_ReturnsEmptyArray_WhenBufferEmpty()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.ReadData(0, 10, null);
            Assert.True(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task WaitForResponse_RequiresPattern()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.WaitForResponse("", 100, "contains");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ClosePort_ReturnsSuccess_WhenNotOpen()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.ClosePort();
            Assert.True(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task SendAndWait_RejectsEmptyData()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.SendAndWait("", "OK");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }
}
