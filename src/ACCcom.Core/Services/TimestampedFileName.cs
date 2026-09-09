namespace ACCcom.Core.Services;

/// <summary>
/// Builds a timestamped default file name (e.g. "ACCCOM_serial_20260909_143000.txt")
/// for save dialogs / recorders. The same "{prefix}_{tag}_{yyyyMMdd_HHmmss}.{ext}"
/// layout was inlined at 6 call sites (DataFlowViewModel ×4, ModbusViewModel ×1,
/// SessionRecorder ×1); centralizing it keeps the timestamp format consistent and
/// unit-testable.
/// </summary>
public static class TimestampedFileName
{
    /// <summary>Builds "prefix[_tag]_yyyyMMdd_HHmmss[.extension]". The tag and
    /// extension segments are omitted when null/empty.</summary>
    public static string Build(string prefix, DateTime now, string? tag = null, string? extension = null)
    {
        var sb = new System.Text.StringBuilder(prefix.Length + 32);
        sb.Append(prefix);
        if (!string.IsNullOrEmpty(tag))
        {
            sb.Append('_').Append(tag);
        }
        sb.Append('_').Append(now.ToString("yyyyMMdd_HHmmss"));
        if (!string.IsNullOrEmpty(extension))
        {
            sb.Append('.').Append(extension);
        }
        return sb.ToString();
    }
}
