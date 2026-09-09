using ACCcom.Core.Services;
using ACCcom.McpServer.Tests.TestHelpers;
using ACCcom.McpServer.Tools;
using Xunit;

namespace ACCcom.McpServer.Tests;

/// <summary>
/// Multi-port MCP support: a tool called with a non-empty tag routes to its own
/// independent serial service and per-tag buffer; the default single-port
/// session (no tag) behaves exactly as before. Tests that fire the static
/// GuiRequested event live in the GuiTriggerContract collection so they cannot
/// cross-pollute the R114 contract assertions.
/// </summary>
[Collection("GuiTriggerContract")]
public class MultiPortToolsTests
{
    [Fact]
    public async Task OpenPort_WithTag_AndListOpenPorts_ReportIt()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        using (sp)
        {
            var tools = new SerialTools(ctx);
            var result = await tools.OpenPort("COM10", 115200, 8, 1, 0, tag: "a");
            Assert.True(ToolContextFactory.ExtractSuccess(result));

            var list = await tools.ListOpenPorts();
            Assert.Contains("\"tag\":\"a\"", list);
            Assert.Contains("\"port\":\"COM10\"", list);
        }
    }

    [Fact]
    public async Task OpenPort_DefaultAndTagged_Coexist()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        using (sp)
        {
            var tools = new SerialTools(ctx);
            Assert.True(ToolContextFactory.ExtractSuccess(await tools.OpenPort("COM3")));
            Assert.True(ToolContextFactory.ExtractSuccess(await tools.OpenPort("COM10", 9600, 8, 1, 0, tag: "b")));

            var list = await tools.ListOpenPorts();
            Assert.Contains("\"tag\":\"\"", list);  // default session
            Assert.Contains("\"tag\":\"b\"", list);
            Assert.Contains("\"port\":\"COM3\"", list);
            Assert.Contains("\"port\":\"COM10\"", list);
        }
    }

    [Fact]
    public async Task Send_ToTag_RoutesToThatPortsService()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        using (sp)
        {
            var tools = new SerialTools(ctx);
            await tools.OpenPort("COM10", tag: "a");
            var result = await tools.Send("HELLO", tag: "a");
            Assert.True(ToolContextFactory.ExtractSuccess(result));

            var service = (VirtualSerialService)ctx.MultiPort.Ports["a"].Service;
            var sent = service.GetSentData();
            Assert.Single(sent);
            Assert.Equal("HELLO", sent[0].Text);
        }
    }

    [Fact]
    public async Task Send_ToUnknownTag_FailsCleanly()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        using (sp)
        {
            var tools = new SerialTools(ctx);
            var result = await tools.Send("HELLO", tag: "ghost");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
            Assert.Contains("ghost", ToolContextFactory.ExtractError(result) ?? "");
        }
    }

    [Fact]
    public async Task ReadData_Tag_IsIsolatedFromDefaultBuffer()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        using (sp)
        {
            var tools = new SerialTools(ctx);
            await tools.OpenPort("COM10", tag: "a");

            // Inject RX on the tagged port only.
            var service = (VirtualSerialService)ctx.MultiPort.Ports["a"].Service;
            service.InjectRxData("41 42"); // "AB"

            var tagged = await tools.ReadData(tag: "a");
            Assert.Contains("\"AB\"", tagged);

            // The default (untagged) buffer must not see the tagged port's data.
            var untagged = await tools.ReadData();
            Assert.DoesNotContain("\"AB\"", untagged);
        }
    }

    [Fact]
    public async Task WaitForResponse_Tag_OnlyMatchesThatPortsData()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        using (sp)
        {
            var tools = new SerialTools(ctx);
            await tools.OpenPort("COM10", tag: "a");

            var service = (VirtualSerialService)ctx.MultiPort.Ports["a"].Service;
            service.InjectRxData("4F 4B"); // "OK"

            var match = await tools.WaitForResponse("OK", tag: "a");
            Assert.Contains("\"matched\":true", match);

            // Default buffer has no data — same pattern must not match there.
            var noMatch = await tools.WaitForResponse("OK", 200);
            Assert.Contains("\"matched\":false", noMatch);
        }
    }

    [Fact]
    public async Task SendAndWait_Tag_SuccessRaisesGuiRequest()
    {
        bool raised = false;
        void Handler() => raised = true;
        SerialTools.GuiRequested += Handler;
        try
        {
            var (ctx, sp) = ToolContextFactory.Create();
            using (sp)
            {
                var tools = new SerialTools(ctx);
                await tools.OpenPort("COM10", tag: "a");
                raised = false; // OpenPort raised; isolate SendAndWait
                var result = await tools.SendAndWait("DATA", "NEVER-MATCHES", tag: "a");
                Assert.True(ToolContextFactory.ExtractSuccess(result));
                Assert.True(raised);
            }
        }
        finally { SerialTools.GuiRequested -= Handler; }
    }

    [Fact]
    public async Task ClearBuffer_Tag_ClearsOnlyThatTag()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        using (sp)
        {
            var tools = new SerialTools(ctx);
            await tools.OpenPort("COM10", tag: "a");
            var service = (VirtualSerialService)ctx.MultiPort.Ports["a"].Service;
            service.InjectRxData("41 42");

            var before = await tools.ReadData(tag: "a");
            Assert.Contains("\"AB\"", before);

            var cleared = await tools.ClearBuffer(target: null, tag: "a");
            Assert.True(ToolContextFactory.ExtractSuccess(cleared));

            var after = await tools.ReadData(tag: "a");
            Assert.DoesNotContain("\"AB\"", after);
        }
    }

    [Fact]
    public void BufferFor_EmptyTag_ReturnsDefaultBuffer()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        using (sp)
        {
            Assert.Same(ctx.Buffer, ctx.BufferFor(""));
            Assert.Same(ctx.Buffer, ctx.BufferFor(null));
        }
    }

    [Fact]
    public void BufferFor_DifferentTags_AreDistinct()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        using (sp)
        {
            var a = ctx.BufferFor("a");
            var b = ctx.BufferFor("b");
            Assert.NotSame(a, b);
            Assert.Same(a, ctx.BufferFor("a")); // cached
        }
    }

    [Fact]
    public async Task ListOpenPorts_Empty_ReturnsZeroCount()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        using (sp)
        {
            var tools = new SerialTools(ctx);
            var list = await tools.ListOpenPorts();
            Assert.Contains("\"count\":0", list);
            Assert.DoesNotContain("\"sensor_a\"", list);
        }
    }

    [Fact]
    public async Task ClosePort_ThenReopenSameTag_DoesNotLeakOldBuffer()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        using (sp)
        {
            var tools = new SerialTools(ctx);

            // First session: open tag "a", receive some RX.
            await tools.OpenPort("COM10", tag: "a");
            var service1 = (VirtualSerialService)ctx.MultiPort.GetPort("a")!.Service;
            service1.InjectRxData("41 42");
            Assert.Contains("\"AB\"", await tools.ReadData(tag: "a"));

            // Close, then reopen the SAME tag.
            Assert.True(ToolContextFactory.ExtractSuccess(await tools.ClosePort(tag: "a")));
            await tools.OpenPort("COM10", tag: "a");

            // The stale "AB" from the first session must NOT be visible.
            var after = await tools.ReadData(tag: "a");
            Assert.DoesNotContain("\"AB\"", after);
        }
    }
}