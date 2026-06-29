using ACCcom.McpServer.Tests.TestHelpers;
using ACCcom.McpServer.Tools;

namespace ACCcom.McpServer.Tests;

public class AnalysisToolsTests
{
    [Fact]
    public async Task AnalyzeProtocol_RequiresNonEmptyFrames()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new AnalysisTools(ctx);
            var result = await tools.AnalyzeProtocol("[]");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task AnalyzeProtocol_RejectsInvalidJson()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new AnalysisTools(ctx);
            var result = await tools.AnalyzeProtocol("not-json");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task CompareFrames_RequiresBothFrames()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new AnalysisTools(ctx);
            var result = await tools.CompareFrames("", "");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task CompareFrames_RejectsInvalidHex()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new AnalysisTools(ctx);
            var result = await tools.CompareFrames("not-hex", "AA55");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task AnalyzeProtocol_WorksInProxyMode_UsingLocalParser()
    {
        // 验证离线分析在代理模式下也能工作(不依赖串口)
        var (ctx, sp) = ToolContextFactory.CreateProxy("http://127.0.0.1:59999");
        try
        {
            var tools = new AnalysisTools(ctx);
            // 代理模式 + 无激活解析器 → 返回错误而非 "not available"
            var result = await tools.AnalyzeProtocol("[\"AA55\"]");
            // 不应包含 "not available in proxy mode"
            Assert.DoesNotContain("not available in proxy mode", result);
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task CompareFrames_WorksInProxyMode()
    {
        var (ctx, sp) = ToolContextFactory.CreateProxy("http://127.0.0.1:59999");
        try
        {
            var tools = new AnalysisTools(ctx);
            var result = await tools.CompareFrames("AA55", "AA55");
            Assert.DoesNotContain("not available in proxy mode", result);
        }
        finally { sp.Dispose(); }
    }
}
