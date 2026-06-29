using ACCcom.McpServer.Tests.TestHelpers;
using ACCcom.McpServer.Tools;

namespace ACCcom.McpServer.Tests;

public class ToolContextTests
{
    [Fact]
    public void CreateDirect_InjectsAllDirectModeServices()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            Assert.False(ctx.UseProxy);
            Assert.NotNull(ctx.Serial);
            Assert.NotNull(ctx.Logger);
            Assert.NotNull(ctx.Stats);
            Assert.NotNull(ctx.MultiPort);
            Assert.NotNull(ctx.AutoBaud);
            Assert.NotNull(ctx.Modbus);
            Assert.NotNull(ctx.SlaveService);
            Assert.NotNull(ctx.Recorder);
            Assert.NotNull(ctx.ParserManager);
            Assert.Null(ctx.Proxy);
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public void CreateProxy_InjectsOnlyProxyServices()
    {
        var (ctx, sp) = ToolContextFactory.CreateProxy("http://127.0.0.1:59999");
        try
        {
            Assert.True(ctx.UseProxy);
            Assert.NotNull(ctx.Proxy);
            Assert.Null(ctx.Serial);
            Assert.Null(ctx.Logger);
            Assert.Null(ctx.Stats);
            Assert.Null(ctx.MultiPort);
            Assert.Null(ctx.AutoBaud);
            Assert.Null(ctx.Modbus);
            Assert.Null(ctx.SlaveService);
            Assert.NotNull(ctx.Recorder);
            Assert.NotNull(ctx.ParserManager);
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public void GetParserEngine_ReturnsActiveEngine_WhenParserNameNullOrEmpty()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var (engine, error) = ctx.GetParserEngine(null);
            Assert.Null(error);
            Assert.Same(ctx.ParserManager.Engine, engine);
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public void GetParserEngine_ReturnsError_WhenParserNotFound()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var (_, error) = ctx.GetParserEngine("does-not-exist");
            Assert.NotNull(error);
            Assert.Contains("not found", error);
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public void RawJson_ProducesCamelCaseJson()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var json = ctx.RawJson(new { success = true, data = new { myField = 1 } });
            Assert.Contains("\"success\":true", json);
            Assert.Contains("\"myField\":1", json);
        }
        finally { sp.Dispose(); }
    }
}
