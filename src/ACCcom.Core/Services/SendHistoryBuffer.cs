namespace ACCcom.Core.Services;

/// <summary>
/// Owns the send-box history list semantics: append-with-dedupe (an existing
/// entry moves to the end instead of duplicating), bounded capacity with
/// oldest-first eviction, and up/down navigation with a "draft" slot past the
/// newest entry. Extracted from DataFlowViewModel so the edge cases (empty
/// history, clamping at both ends, dedupe + cap interplay) are unit-testable
/// without a UI layer.
/// </summary>
public class SendHistoryBuffer
{
    private readonly List<string> _entries = new();
    private readonly int _capacity;
    private int _index;

    /// <summary>Creates a history buffer. <paramref name="capacity"/> is clamped
    /// to at least 1 so a misconfigured setting cannot disable history.</summary>
    public SendHistoryBuffer(int capacity)
    {
        _capacity = Math.Max(1, capacity);
        _index = _entries.Count;
    }

    /// <summary>Snapshot of entries, oldest first. The caller owns the copy.</summary>
    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_entries) return _entries.ToArray();
        }
    }

    public int Count
    {
        get { lock (_entries) return _entries.Count; }
    }

    /// <summary>Appends <paramref name="text"/> with move-to-end dedupe and
    /// capacity eviction; resets navigation to the "draft" slot. Empty and
    /// whitespace entries are ignored, mirroring the send-box guard.</summary>
    public void Add(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (_entries)
        {
            var existing = _entries.IndexOf(text);
            if (existing >= 0) _entries.RemoveAt(existing);
            _entries.Add(text);

            while (_entries.Count > _capacity)
                _entries.RemoveAt(0);

            _index = _entries.Count;
        }
    }

    /// <summary>Clears all entries and resets navigation.</summary>
    public void Clear()
    {
        lock (_entries)
        {
            _entries.Clear();
            _index = 0;
        }
    }

    /// <summary>Moves the navigation index by <paramref name="direction"/>
    /// (typically ±1 for Up/Down keys) and returns the entry to restore, or
    /// null when there is no history at all. On a true return,
    /// <paramref name="text"/> holds the entry (empty string means "draft"
    /// past the newest entry) and <paramref name="caretIndex"/> is the caret
    /// position the view should restore (end of text, so Enter re-sends).</summary>
    public bool TryNavigate(int direction, out string? text, out int caretIndex)
    {
        lock (_entries)
        {
            if (_entries.Count == 0)
            {
                text = null;
                caretIndex = 0;
                return false;
            }

            _index = Math.Clamp(_index + direction, 0, _entries.Count);
            if (_index < _entries.Count)
            {
                text = _entries[_index];
                caretIndex = text.Length;
            }
            else
            {
                text = "";
                caretIndex = 0;
            }
            return true;
        }
    }
}
