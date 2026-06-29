using ACCcom.McpServer.Tests.TestHelpers;
using ACCcom.McpServer.Tools;

namespace ACCcom.McpServer.Tests;

public class ParserToolsTests
{
    [Fact]
    public async Task ListParsers_ReturnsAvailableParsers()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new ParserTools(ctx);
            var result = await tools.ListParsers();
            Assert.True(ToolContextFactory.ExtractSuccess(result));
            // 内置解析器 sample.csx 应存在
            Assert.Contains("sample", result);
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReadParser_ReturnsSource_WhenExists()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new ParserTools(ctx);
            var result = await tools.ReadParser("sample");
            Assert.True(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReadParser_ReturnsError_WhenNotFound()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new ParserTools(ctx);
            var result = await tools.ReadParser("no-such-parser");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task WriteParser_RequiresNameAndCode()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new ParserTools(ctx);
            var result = await tools.WriteParser("", "");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ParseRaw_RequiresHex()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new ParserTools(ctx);
            var result = await tools.ParseRaw("");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ParseRaw_ReturnsError_OnInvalidHex()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new ParserTools(ctx);
            var result = await tools.ParseRaw("not-hex");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task GenerateParser_RequiresBothArgs()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new ParserTools(ctx);
            var result = await tools.GenerateParser("", "");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task GetSchemaTemplate_ReturnsTemplate()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new ParserTools(ctx);
            var result = await tools.GetSchemaTemplate();
            Assert.True(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }
}
