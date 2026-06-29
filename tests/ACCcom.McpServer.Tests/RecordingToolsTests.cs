using ACCcom.McpServer.Tests.TestHelpers;
using ACCcom.McpServer.Tools;

namespace ACCcom.McpServer.Tests;

public class RecordingToolsTests
{
    [Fact]
    public async Task RecordingStatus_InitiallyNotRecording()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new RecordingTools(ctx);
            var result = await tools.RecordingStatus();
            Assert.True(ToolContextFactory.ExtractSuccess(result));
            Assert.Contains("\"isRecording\":false", result);
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task StopRecording_WhenNotRecording_ReturnsError()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new RecordingTools(ctx);
            var result = await tools.StopRecording();
            Assert.False(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReplaySession_RequiresFilename()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new RecordingTools(ctx);
            var result = await tools.ReplaySession("");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReplaySession_NonExistentFile_ReturnsEmpty()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new RecordingTools(ctx);
            var result = await tools.ReplaySession("non-existent-file.jsonl");
            Assert.True(ToolContextFactory.ExtractSuccess(result));
            Assert.Contains("\"count\":0", result);
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task StartRecording_StartsAndStopsCleanly()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new RecordingTools(ctx);
            // 显式指定临时目录,避免 LocalApplicationData 权限问题
            var tempFile = Path.Combine(Path.GetTempPath(), $"ACCCOM_test_{Guid.NewGuid():N}.jsonl");
            var start = await tools.StartRecording(tempFile);
            Assert.True(ToolContextFactory.ExtractSuccess(start), $"start failed: {start}");

            var status = await tools.RecordingStatus();
            Assert.Contains("\"isRecording\":true", status);

            var stop = await tools.StopRecording();
            Assert.True(ToolContextFactory.ExtractSuccess(stop));
            Assert.Contains("stopped", stop);
        }
        finally { sp.Dispose(); }
    }
}
