using ACCcom.Core.Services;
using ACCcom.McpServer.Tests.TestHelpers;
using ACCcom.McpServer.Tools;

namespace ACCcom.McpServer.Tests;

public class ToolContextTests
{
    [Fact]
    public void Constructor_null_injections_throw()
    {
        // The constructor subscribes to both services' events; a null would NRE
        // there instead of at the contract boundary.
        Assert.Throws<ArgumentNullException>(() => new ToolContext(null!, new VirtualSerialService()));
        var multiPort = new MultiPortService(() => new VirtualSerialService());
        Assert.Throws<ArgumentNullException>(() => new ToolContext(multiPort, null!));
    }

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

    [Fact]
    public void RemoveBuffer_ClearsTaggedBuffer_CreatesFresh()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var a = ctx.BufferFor("a");
            Assert.Same(a, ctx.BufferFor("a"));

            // Remove, then BufferFor creates a NEW instance.
            ctx.RemoveBuffer("a");
            var fresh = ctx.BufferFor("a");
            Assert.NotSame(a, fresh);
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public void RemoveBuffer_EmptyOrUnknownTag_IsSafeNoOp()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            // Empty/null tags hit the default buffer — no removal.
            var buffer = ctx.Buffer;
            ctx.RemoveBuffer("");
            ctx.RemoveBuffer(null);
            Assert.Same(buffer, ctx.Buffer);

            // Unknown tag: no throw.
            ctx.RemoveBuffer("ghost");
        }
        finally { sp.Dispose(); }
    }
}
