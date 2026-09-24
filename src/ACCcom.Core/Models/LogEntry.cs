using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ACCcom.Core.Models;

public class LogEntry : INotifyPropertyChanged
{
    /// <summary>Display-level tag used by the UI when an entry arrives without a
    /// per-port tag (e.g. from the default single-port session). Kept in Core so
    /// the convention is shared by every UI surface that normalizes empty tags.</summary>
    public const string MainPortTag = "main";

    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Direction { get; set; } = ""; // "RX" or "TX"
    public string PortTag { get; set; } = "";
    public string RawHex { get; set; } = "";
    public string Text { get; set; } = "";
    public List<FieldAnnotation>? Fields { get; set; }
    public string? HighlightColor { get; set; }

    private bool _isSearchMatch;
    public bool IsSearchMatch
    {
        get => _isSearchMatch;
        set
        {
            if (_isSearchMatch == value) return;
            _isSearchMatch = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Raises PropertyChanged for <see cref="HighlightColor"/>. The setter is a
    /// plain auto-property on the receive hot path (no notify per packet); the
    /// UI calls this after a batch recompute — highlight-rule edits and theme
    /// switches — so visible rows re-run their Foreground converter even when
    /// the hex string itself did not change (the converter's output depends on
    /// the active theme's background).
    /// </summary>
    public void NotifyHighlightChanged() => OnPropertyChanged(nameof(HighlightColor));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
