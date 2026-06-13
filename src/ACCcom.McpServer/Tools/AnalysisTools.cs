using System.ComponentModel;
using System.Text.Json;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using ModelContextProtocol.Server;

namespace ACCcom.McpServer.Tools;

[McpServerToolType]
public class AnalysisTools
{
    private readonly ToolContext _ctx;
    private ParserManager _parserManager => _ctx.ParserManager;

    public AnalysisTools(ToolContext ctx)
    {
        _ctx = ctx;
    }

    [McpServerTool, Description("Analyze protocol data by parsing multiple hex frames. Returns field statistics, error distribution, and parsed results. Parameters: hexFrames (JSON array of hex strings), parserName (optional).")]
    public async Task<string> AnalyzeProtocol(
        [Description("JSON array of hex strings, e.g. [\"AA5503\", \"AA5504\"]")] string hexFrames,
        [Description("Parser name (null = use active parser)")] string? parserName = null)
    {
        if (_ctx.UseProxy)
            return _ctx.RawJson(new { success = false, error = "Protocol analysis not available in proxy mode" });

        List<string> frames;
        try { frames = JsonSerializer.Deserialize<List<string>>(hexFrames) ?? new(); }
        catch (Exception ex) { return _ctx.RawJson(new { success = false, error = $"Invalid hexFrames JSON: {ex.Message}" }); }

        if (frames.Count == 0)
            return _ctx.RawJson(new { success = false, error = "hexFrames array is empty" });

        var (engine, engineError) = _ctx.GetParserEngine(parserName);
        if (engineError != null)
            return _ctx.RawJson(new { success = false, error = engineError });
        if (_parserManager.ActiveParserName == null && string.IsNullOrEmpty(parserName))
            return _ctx.RawJson(new { success = false, error = "No active parser. Use activate_parser first or specify parserName." });

        var allResults = new List<object>();
        var fieldStats = new Dictionary<string, FieldAccumulator>();
        int errorFrames = 0, totalBytes = 0;

        foreach (var hex in frames)
        {
            byte[] data;
            try { data = Convert.FromHexString(hex.Replace(" ", "")); }
            catch { allResults.Add(new { hex, error = "Invalid hex" }); continue; }

            totalBytes += data.Length;
            var fields = await engine.ExecuteAsync(data, DateTime.Now).ConfigureAwait(false);
            var fieldList = fields ?? new();
            bool hasError = fieldList.Any(f => f.Severity == FieldSeverity.Error);
            if (hasError) errorFrames++;

            allResults.Add(new { hex, byteCount = data.Length, fields = fieldList, hasError });

            foreach (var f in fieldList)
            {
                if (!fieldStats.TryGetValue(f.Name, out var acc))
                {
                    acc = new FieldAccumulator();
                    fieldStats[f.Name] = acc;
                }
                acc.Count++;
                if (double.TryParse(f.DisplayValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val))
                {
                    if (acc.MinValue == null || val < acc.MinValue) acc.MinValue = val;
                    if (acc.MaxValue == null || val > acc.MaxValue) acc.MaxValue = val;
                    acc.Sum += val;
                }
            }
        }

        var stats = fieldStats.Select(kv => new
        {
            name = kv.Key,
            count = kv.Value.Count,
            minValue = kv.Value.MinValue,
            maxValue = kv.Value.MaxValue,
            avgValue = kv.Value.Count > 0 && kv.Value.Sum.HasValue ? Math.Round(kv.Value.Sum.Value / kv.Value.Count, 4) : (double?)null
        });

        return _ctx.RawJson(new
        {
            success = true,
            data = new
            {
                totalFrames = frames.Count,
                totalBytes,
                errorFrames,
                fieldStats = stats,
                results = allResults
            }
        });
    }

    [McpServerTool, Description("Compare two hex frames field-by-field using the active parser. Shows differences in field values. Parameters: hex1, hex2, parserName (optional).")]
    public async Task<string> CompareFrames(
        [Description("First hex frame (e.g. 'AA 55 03 01 19 2E')")] string hex1,
        [Description("Second hex frame (e.g. 'AA 55 03 02 1A 2F')")] string hex2,
        [Description("Parser name (null = use active parser)")] string? parserName = null)
    {
        if (_ctx.UseProxy)
            return _ctx.RawJson(new { success = false, error = "Frame comparison not available in proxy mode" });

        if (string.IsNullOrEmpty(hex1) || string.IsNullOrEmpty(hex2))
            return _ctx.RawJson(new { success = false, error = "Both hex1 and hex2 are required" });

        var (engine, engineError) = _ctx.GetParserEngine(parserName);
        if (engineError != null)
            return _ctx.RawJson(new { success = false, error = engineError });
        if (_parserManager.ActiveParserName == null && string.IsNullOrEmpty(parserName))
            return _ctx.RawJson(new { success = false, error = "No active parser. Use activate_parser first or specify parserName." });

        byte[] data1, data2;
        try { data1 = Convert.FromHexString(hex1.Replace(" ", "")); }
        catch { return _ctx.RawJson(new { success = false, error = "Invalid hex1" }); }
        try { data2 = Convert.FromHexString(hex2.Replace(" ", "")); }
        catch { return _ctx.RawJson(new { success = false, error = "Invalid hex2" }); }

        var fields1 = await engine.ExecuteAsync(data1, DateTime.Now).ConfigureAwait(false) ?? new();
        var fields2 = await engine.ExecuteAsync(data2, DateTime.Now).ConfigureAwait(false) ?? new();

        var map1 = fields1.ToDictionary(f => f.Name);
        var map2 = fields2.ToDictionary(f => f.Name);
        var allNames = map1.Keys.Union(map2.Keys).OrderBy(n => n).ToList();

        var comparisons = allNames.Select(name =>
        {
            map1.TryGetValue(name, out var f1);
            map2.TryGetValue(name, out var f2);

            if (f1 == null) return new { field = name, status = "onlyInFrame2", value1 = (string?)null, value2 = (string?)f2!.DisplayValue, delta = (double?)null };
            if (f2 == null) return new { field = name, status = "onlyInFrame1", value1 = (string?)f1.DisplayValue, value2 = (string?)null, delta = (double?)null };

            bool same = string.Equals(f1.DisplayValue, f2.DisplayValue, StringComparison.Ordinal);
            double? delta = null;
            if (!same && double.TryParse(f1.DisplayValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v1)
                && double.TryParse(f2.DisplayValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v2))
                delta = Math.Round(v2 - v1, 6);

            return new { field = name, status = same ? "match" : "different", value1 = (string?)f1.DisplayValue, value2 = (string?)f2.DisplayValue, delta };
        }).ToList();

        int diffCount = comparisons.Count(c => c.status == "different");
        int onlyIn1 = comparisons.Count(c => c.status == "onlyInFrame1");
        int onlyIn2 = comparisons.Count(c => c.status == "onlyInFrame2");

        return _ctx.RawJson(new
        {
            success = true,
            data = new
            {
                hex1, hex2,
                totalFields = comparisons.Count,
                matching = comparisons.Count - diffCount - onlyIn1 - onlyIn2,
                different = diffCount,
                onlyInFrame1 = onlyIn1,
                onlyInFrame2 = onlyIn2,
                comparisons
            }
        });
    }
}

internal class FieldAccumulator
{
    public int Count { get; set; }
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public double? Sum { get; set; }
}
