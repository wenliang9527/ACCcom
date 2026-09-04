using ACCcom.McpServer.Tests.TestHelpers;
using ACCcom.McpServer.Tools;

namespace ACCcom.McpServer.Tests;

public class ToolContextTests
{
    [Fact]
    public void Create_InjectsSerialService()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            Assert.NotNull(ctx.Serial);
            Assert.NotNull(ctx.Buffer);
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public void RawJson_ProducesCamelCaseJson()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var json = ctx.RawJson(new { success = true, data = new { myField = 1 } });
            Assert.Contains("\"success\":true", json);
            Assert.Contains("\"myField\":1", json);
        }
        finally { sp.Dispose(); }
    }
}
