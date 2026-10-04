using System.Text;

namespace ACCcom.Core.Services;

/// <summary>Byte-exact line splitting for tailing the shared MCP traffic JSONL.
/// The GUI tail reads with byte offsets (Seek/fs.Length), but the writer's
/// buffered stream can leave a record torn mid-file between flushes: reading
/// lines with a StreamReader and then trusting fs.Length loses any record
/// that straddled the read (the offset skips past the headless remainder,
/// which can never parse). Splitting on the last '\n' keeps the offset on a
/// line boundary and holds the partial tail back until the writer completes
/// it — the next read picks the whole record up.</summary>
public static class TrafficLogTail
{
    /// <summary>Splits buffer[0..count) into complete lines plus the new byte
    /// offset. Lines are UTF-8 decoded; a UTF-8 BOM is stripped only at chunk
    /// start when <paramref name="startOffset"/> is 0 (it precedes line 1 and
    /// would otherwise corrupt the first parse). '\n' terminates; a preceding
    /// '\r' is trimmed. When the chunk holds no complete line the returned
    /// offset equals <paramref name="startOffset"/> and the caller waits for
    /// more bytes.</summary>
    public static (IReadOnlyList<string> Lines, long NewOffset) Split(byte[] buffer, int count, long startOffset)
    {
        if (count <= 0)
            return (Array.Empty<string>(), startOffset);

        var lastNewline = Array.LastIndexOf(buffer, (byte)'\n', count - 1, count);
        if (lastNewline < 0)
            return (Array.Empty<string>(), startOffset);

        var end = lastNewline + 1;
        var skip = 0;
        if (startOffset == 0 && count >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            skip = 3;

        var text = Encoding.UTF8.GetString(buffer, skip, end - skip);
        var pieces = text.Split('\n');
        // The chunk always ends with '\n', so the final split piece is an
        // artifact, not a line. Interior blanks stay: TryParseLine skips them.
        var lines = new string[pieces.Length - 1];
        for (var i = 0; i < lines.Length; i++)
            lines[i] = pieces[i].EndsWith('\r') ? pieces[i][..^1] : pieces[i];
        return (lines, startOffset + end);
    }
}
