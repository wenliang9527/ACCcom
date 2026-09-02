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
            // 只传纯文件名：录制文件固定写入 %LOCALAPPDATA%\ACCcom\recordings 目录
            var filename = $"ACCCOM_test_{Guid.NewGuid():N}.jsonl";
            var start = await tools.StartRecording(filename);
            Assert.True(ToolContextFactory.ExtractSuccess(start), $"start failed: {start}");

            var status = await tools.RecordingStatus();
            Assert.Contains("\"isRecording\":true", status);

            var stop = await tools.StopRecording();
            Assert.True(ToolContextFactory.ExtractSuccess(stop));
            Assert.Contains("stopped", stop);
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task StartRecording_PathTraversal_Rejected()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new RecordingTools(ctx);
            var result = await tools.StartRecording(@"..\..\evil.jsonl");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
            Assert.Contains("Invalid recording filename", result);

            var absolute = Path.Combine(Path.GetTempPath(), "ACCCOM_evil.jsonl");
            var result2 = await tools.StartRecording(absolute);
            Assert.False(ToolContextFactory.ExtractSuccess(result2));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReplaySession_PathTraversal_Rejected()
    {
        var (ctx, sp) = ToolContextFactory.CreateDirect();
        try
        {
            var tools = new RecordingTools(ctx);
            var result = await tools.ReplaySession(@"C:\Windows\win.ini");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
            Assert.Contains("Invalid recording filename", result);
        }
        finally { sp.Dispose(); }
    }
}
