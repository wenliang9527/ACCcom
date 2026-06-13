namespace ACCcom.Core.Models;

public class BookmarkItem
{
    public int EntryId { get; set; }
    public string Label { get; set; } = "";
    public string Direction { get; set; } = ""; // "RX" or "TX"
    public DateTime Timestamp { get; set; }
    public string Preview { get; set; } = ""; // first 50 chars of Text
}
