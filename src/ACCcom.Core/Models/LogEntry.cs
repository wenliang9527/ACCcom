using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ACCcom.Core.Models;

public class LogEntry : INotifyPropertyChanged
{
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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
