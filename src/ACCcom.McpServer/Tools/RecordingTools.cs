using System.ComponentModel;
using ModelContextProtocol.Server;

namespace ACCcom.McpServer.Tools;

[McpServerToolType]
public class RecordingTools
{
    private readonly ToolContext _ctx;

    public RecordingTools(ToolContext ctx)
    {
        _ctx = ctx;
    }

    [McpServerTool, Description("Start recording all serial communication to a JSONL file. Parameters: filename (optional, auto-generated if empty).")]
    public Task<string> StartRecording([Description("Filename for recording (optional)")] string? filename = null)
    {
        if (_ctx.Recorder.IsRecording)
            return Task.FromResult(_ctx.RawJson(new { success = false, error = "Already recording", file = _ctx.Recorder.CurrentFile, recordedCount = _ctx.Recorder.RecordedCount }));

        if (_ctx.Recorder.StartRecording(filename))
            return Task.FromResult(_ctx.RawJson(new { success = true, data = new { message = "Recording started", file = _ctx.Recorder.CurrentFile } }));
        return Task.FromResult(_ctx.RawJson(new { success = false, error = "Failed to start recording" }));
    }

    [McpServerTool, Description("Stop the current recording session.")]
    public Task<string> StopRecording()
    {
        if (!_ctx.Recorder.IsRecording)
            return Task.FromResult(_ctx.RawJson(new { success = false, error = "Not currently recording" }));

        var file = _ctx.Recorder.CurrentFile;
        var count = _ctx.Recorder.RecordedCount;
        _ctx.Recorder.StopRecording();
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { message = "Recording stopped", file, recordedCount = count } }));
    }

    [McpServerTool, Description("Replay a recorded session file. Returns all entries. Parameters: filename (path to .jsonl file).")]
    public Task<string> ReplaySession([Description("Path to the .jsonl recording file")] string filename)
    {
        if (string.IsNullOrEmpty(filename))
            return Task.FromResult(_ctx.RawJson(new { success = false, error = "Filename is required" }));

        var entries = _ctx.Recorder.ReplayFile(filename);
        return Task.FromResult(_ctx.RawJson(new { success = true, data = new { file = filename, entries, count = entries.Count } }));
    }
}
