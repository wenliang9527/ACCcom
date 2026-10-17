using ACCcom.Core.Models;
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

    [Fact]
    public void BufferFor_Released_WhenPortClosed_ViaMultiPortService()
    {
        // Every close path must release the per-tag buffer (~120 KB each).
        // Previously only the MCP close_port tool called RemoveBuffer, so a
        // port closed through MultiPortService directly (or a serial drop)
        // leaked its buffer for the life of the process.
        var multiPort = new MultiPortService(() => new VirtualSerialService());
        var ctx = new ToolContext(multiPort, new VirtualSerialService());
        multiPort.OpenPort("t1", new SerialConfig { PortName = "COM1", BaudRate = 115200 });
        Assert.NotNull(ctx.BufferFor("t1"));

        multiPort.ClosePort("t1"); // NOT the MCP close_port tool

        Assert.False(ctx.Buffers.ContainsKey("t1"));
    }

    [Fact]
    public void BufferFor_Released_WhenPortDrops_SerialDisconnect()
    {
        // A serial-level disconnect (unplug/error) must also release the
        // per-tag buffer.
        var multiPort = new MultiPortService(() => new VirtualSerialService());
        var ctx = new ToolContext(multiPort, new VirtualSerialService());
        multiPort.OpenPort("t1", new SerialConfig { PortName = "COM1", BaudRate = 115200 });
        Assert.NotNull(ctx.BufferFor("t1"));

        var service = (VirtualSerialService)multiPort.GetPort("t1")!.Service;
        service.Close(); // serial-level disconnect (not a MultiPortService close)

        Assert.False(ctx.Buffers.ContainsKey("t1"));
    }

    [Fact]
    public void BufferFor_CapsTrackedTags_ForLivePorts()
    {
        // Each tracked tag costs ~120 KB; the set must stay bounded even when
        // a client opens more live ports than the cap.
        var multiPort = new MultiPortService(() => new VirtualSerialService());
        var ctx = new ToolContext(multiPort, new VirtualSerialService());
        for (var i = 0; i < 80; i++)
        {
            multiPort.OpenPort($"t{i}", new SerialConfig { PortName = $"COM{i % 20}", BaudRate = 115200 });
            ctx.BufferFor($"t{i}");
        }

        Assert.True(ctx.Buffers.Count <= 64, $"tracked buffers should be capped, got {ctx.Buffers.Count}");
    }
}
