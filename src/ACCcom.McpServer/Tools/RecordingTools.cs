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
    public async Task<string> StartRecording([Description("Filename for recording (optional)")] string? filename = null)
    {
        if (_ctx.UseProxy)
            return await _ctx.Proxy!.PostAsync("/api/recording/start", new { filename }).ConfigureAwait(false);

        if (_ctx.Recorder.IsRecording)
            return _ctx.RawJson(new { success = false, error = "Already recording", file = _ctx.Recorder.CurrentFile, recordedCount = _ctx.Recorder.RecordedCount });

        if (_ctx.Recorder.StartRecording(filename))
            return _ctx.RawJson(new { success = true, data = new { message = "Recording started", file = _ctx.Recorder.CurrentFile } });
        return _ctx.RawJson(new { success = false, error = "Failed to start recording" });
    }

    [McpServerTool, Description("Stop the current recording session.")]
    public async Task<string> StopRecording()
    {
        if (_ctx.UseProxy)
            return await _ctx.Proxy!.PostAsync("/api/recording/stop").ConfigureAwait(false);

        if (!_ctx.Recorder.IsRecording)
            return _ctx.RawJson(new { success = false, error = "Not currently recording" });

        var file = _ctx.Recorder.CurrentFile;
        var count = _ctx.Recorder.RecordedCount;
        _ctx.Recorder.StopRecording();
        return _ctx.RawJson(new { success = true, data = new { message = "Recording stopped", file, recordedCount = count } });
    }

    [McpServerTool, Description("Replay a recorded session file. Returns all entries. Parameters: filename (path to .jsonl file).")]
    public async Task<string> ReplaySession([Description("Path to the .jsonl recording file")] string filename)
    {
        if (_ctx.UseProxy)
            return await _ctx.Proxy!.PostAsync("/api/recording/replay", new { filename }).ConfigureAwait(false);

        if (string.IsNullOrEmpty(filename))
            return _ctx.RawJson(new { success = false, error = "Filename is required" });

        var entries = _ctx.Recorder.ReplayFile(filename);
        return _ctx.RawJson(new { success = true, data = new { file = filename, entries, count = entries.Count } });
    }

    [McpServerTool, Description("Get current recording status: whether recording is active, current file, and recorded entry count.")]
    public async Task<string> RecordingStatus()
    {
        if (_ctx.UseProxy)
            return await _ctx.Proxy!.GetAsync("/api/recording/status").ConfigureAwait(false);

        return _ctx.RawJson(new
        {
            success = true,
            data = new
            {
                isRecording = _ctx.Recorder.IsRecording,
                file = _ctx.Recorder.CurrentFile,
                recordedCount = _ctx.Recorder.RecordedCount
            }
        });
    }
}
