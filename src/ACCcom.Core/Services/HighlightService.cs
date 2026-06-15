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

        var matchingRules = Rules
            .Where(r => r.IsEnabled && MatchesRule(r, entry))
            .OrderByDescending(r => r.Priority);

        return matchingRules.FirstOrDefault()?.Color;
    }

    private bool MatchesRule(HighlightRule rule, LogEntry entry)
    {
        if (rule.Direction.HasValue)
        {
            var ruleDir = rule.Direction.Value == HighlightDirection.RX ? "RX" : "TX";
            if (entry.Direction != ruleDir) return false;
        }

        var targetText = rule.MatchHex ? entry.RawHex : entry.Text;
        if (string.IsNullOrEmpty(targetText)) return false;

        return rule.MatchType switch
        {
            HighlightMatchType.Contains => targetText.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase),
            HighlightMatchType.Exact => targetText.Equals(rule.Pattern, StringComparison.OrdinalIgnoreCase),
            HighlightMatchType.Regex => Regex.IsMatch(targetText, rule.Pattern),
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
