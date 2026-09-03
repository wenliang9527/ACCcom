using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public enum HighlightMatchType
{
    Contains,
    Regex,
    Exact
}

public enum HighlightDirection
{
    RX,
    TX
}

public class HighlightRule
{
    public string Name { get; set; } = "";
    public string Pattern { get; set; } = "";
    public string Color { get; set; } = "#FF0000";
    public int Priority { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool MatchHex { get; set; }
    public HighlightDirection? Direction { get; set; }
    public HighlightMatchType MatchType { get; set; } = HighlightMatchType.Contains;
}

public class HighlightService
{
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    public ObservableCollection<HighlightRule> Rules { get; } = new();

    private readonly string _filePath;

    public HighlightService()
    {
        _filePath = Path.Combine(AppContext.BaseDirectory, "highlights.json");
    }

    public HighlightService(string filePath)
    {
        _filePath = filePath;
    }

    public void AddRule(HighlightRule rule)
    {
        var existing = Rules.FirstOrDefault(r => r.Name == rule.Name);
        if (existing != null)
            Rules.Remove(existing);
        Rules.Add(rule);
    }

    public bool RemoveRule(string name)
    {
        var rule = Rules.FirstOrDefault(r => r.Name == name);
        if (rule == null) return false;
        Rules.Remove(rule);
        return true;
    }

    public string? GetHighlightColor(LogEntry entry)
    {
        if (entry == null) return null;

        // Hand-rolled scan instead of LINQ Where+OrderByDescending: this runs on
        // the UI thread for every frame received (via DataFlowViewModel.ApplyHighlight),
        // and the LINQ pipeline allocated an enumerable + sort per call. We track the
        // highest-priority match in one pass with no per-call allocation. OrderByDescending
        // is stable, so equal priorities keep insertion order — we mirror that by only
        // replacing a tie when there is no current best.
        string? best = null;
        var bestPriority = int.MinValue;
        foreach (var rule in Rules)
        {
            if (!rule.IsEnabled) continue;
            if (rule.Direction.HasValue)
            {
                var ruleDir = rule.Direction.Value == HighlightDirection.RX ? "RX" : "TX";
                if (entry.Direction != ruleDir) continue; // cheap filter before text work
            }
            // Cannot beat the current best (strictly lower, or tied-but-earlier-wins).
            if (best != null && rule.Priority <= bestPriority) continue;

            var targetText = rule.MatchHex ? entry.RawHex : entry.Text;
            if (string.IsNullOrEmpty(targetText)) continue;

            if (MatchesPattern(targetText, rule.Pattern, rule.MatchType))
            {
                best = rule.Color;
                bestPriority = rule.Priority;
            }
        }
        return best;
    }

    private static bool MatchesPattern(string target, string pattern, HighlightMatchType matchType)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        return matchType switch
        {
            HighlightMatchType.Contains => target.Contains(pattern, StringComparison.OrdinalIgnoreCase),
            HighlightMatchType.Exact => target.Equals(pattern, StringComparison.OrdinalIgnoreCase),
            HighlightMatchType.Regex => Regex.IsMatch(target, pattern),
            _ => false
        };
    }

    public void Load()
    {
        Rules.Clear();
        if (!File.Exists(_filePath)) return;

        try
        {
            var json = File.ReadAllText(_filePath);
            var rules = JsonSerializer.Deserialize<HighlightRule[]>(json);
            if (rules != null)
            {
                foreach (var rule in rules)
                    Rules.Add(rule);
            }
        }
        catch
        {
            // Ignore corrupt file, start fresh
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Rules.ToArray(), IndentedOptions);
        File.WriteAllText(_filePath, json);
    }
}
