using ACCcom.McpServer.Tests.TestHelpers;
using ACCcom.McpServer.Tools;
using Xunit;

namespace ACCcom.McpServer.Tests;

/// <summary>
/// The static SerialTools.GuiRequested event is shared process-wide, so tests
/// that subscribe to it must not run in parallel with each other. xUnit runs
/// collections one class at a time; the parallel-assembly default still lets
/// OTHER test classes run concurrently (they never fire the event).
/// </summary>
[CollectionDefinition("GuiTriggerContract", DisableParallelization = true)]
public class GuiTriggerContractCollection { }

/// <summary>
/// Locks the R114 contract: the GUI is only launched on real serial
/// communication (OpenPort success, Send success, SendAndWait after a
/// successful send), never for read-only/list tools, and never on failure
/// paths. The static GuiRequested event is checked directly — GuiNotifier
/// itself Process.Starts the GUI, so it is intentionally left untested.
///
/// SerialTools.GuiRequested is a process-wide static, so these tests MUST NOT
/// run in parallel with each other (or with any other test that fires it): a
/// concurrently raised event would set another test's "did not raise" flag.
/// </summary>
[Collection("GuiTriggerContract")]
public class GuiTriggerContractTests
{
    [Fact]
    public async Task OpenPort_Success_RaisesGuiRequest()
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
                var result = await tools.OpenPort("COM10", 9600, 8, 1, 0);
                Assert.True(ToolContextFactory.ExtractSuccess(result));
                Assert.True(raised);
            }
        }
        finally { SerialTools.GuiRequested -= Handler; }
    }

    [Fact]
    public async Task OpenPort_Failure_DoesNotRaise()
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
                // Empty port name is rejected before any Open attempt.
                var result = await tools.OpenPort("");
                Assert.False(ToolContextFactory.ExtractSuccess(result));
                Assert.False(raised);
            }
        }
        finally { SerialTools.GuiRequested -= Handler; }
    }

    [Fact]
    public async Task Send_Success_RaisesGuiRequest()
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
                await tools.OpenPort("COM10"); // open first so Send succeeds
                raised = false; // OpenPort itself raises; isolate Send
                var result = await tools.Send("ABC", false);
                Assert.True(ToolContextFactory.ExtractSuccess(result));
                Assert.True(raised);
            }
        }
        finally { SerialTools.GuiRequested -= Handler; }
    }

    [Fact]
    public async Task Send_EmptyData_DoesNotRaise()
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
                // Empty data is rejected before the port is touched, so no
                // open/send happens and nothing may raise.
                var result = await tools.Send("");
                Assert.False(ToolContextFactory.ExtractSuccess(result));
                Assert.False(raised);
            }
        }
        finally { SerialTools.GuiRequested -= Handler; }
    }

    [Fact]
    public async Task SendAndWait_SuccessfulSend_RaisesGuiRequest()
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
                await tools.OpenPort("COM10");
                raised = false; // OpenPort already raised; isolate SendAndWait
                // The send succeeds even though no response will match; the
                // GuiRequest must fire as soon as the frame goes out.
                var result = await tools.SendAndWait("DATA", "NEVER-MATCHES", false, 300);
                Assert.True(ToolContextFactory.ExtractSuccess(result));
                Assert.True(raised);
            }
        }
        finally { SerialTools.GuiRequested -= Handler; }
    }

    [Fact]
    public async Task ListPorts_DoesNotRaise()
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
                var result = await tools.ListPorts();
                Assert.True(ToolContextFactory.ExtractSuccess(result));
                Assert.False(raised);
            }
        }
        finally { SerialTools.GuiRequested -= Handler; }
    }

    [Fact]
    public async Task ReadData_DoesNotRaise()
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
                var result = await tools.ReadData(0, 10, null);
                Assert.True(ToolContextFactory.ExtractSuccess(result));
                Assert.False(raised);
            }
        }
        finally { SerialTools.GuiRequested -= Handler; }
    }

    [Fact]
    public async Task ClearBuffer_DoesNotRaise()
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
                var result = await tools.ClearBuffer("all");
                Assert.True(ToolContextFactory.ExtractSuccess(result));
                Assert.False(raised);
            }
        }
        finally { SerialTools.GuiRequested -= Handler; }
    }

    [Fact]
    public async Task WaitForResponse_DoesNotRaise()
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
                var result = await tools.WaitForResponse("SOMETHING", 200, "contains");
                Assert.True(ToolContextFactory.ExtractSuccess(result)); // matched=false is still success
                Assert.False(raised);
            }
        }
        finally { SerialTools.GuiRequested -= Handler; }
    }

    [Fact]
    public async Task OpenPort_WithTag_Success_RaisesGuiRequest()
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
                var result = await tools.OpenPort("COM10", 115200, 8, 1, 0, tag: "sensor_a");
                Assert.True(ToolContextFactory.ExtractSuccess(result));
                Assert.True(raised);
            }
        }
        finally { SerialTools.GuiRequested -= Handler; }
    }

    [Fact]
    public async Task ClosePort_WithTag_DoesNotRaise()
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
                await tools.OpenPort("COM10", tag: "sensor_a");
                raised = false; // OpenPort raised; isolate ClosePort
                var result = await tools.ClosePort(tag: "sensor_a");
                Assert.True(ToolContextFactory.ExtractSuccess(result));
                Assert.False(raised);
            }
        }
        finally { SerialTools.GuiRequested -= Handler; }
    }
}