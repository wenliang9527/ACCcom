using System.Text.RegularExpressions;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class TriggerService
{
    private readonly List<TriggerRule> _rules = new();
    private readonly object _lock = new();

    public event Action<TriggerRule, LogEntry>? OnTriggerFired;

    public void AddRule(TriggerRule rule)
    {
        lock (_lock) { _rules.Add(rule); }
    }

    public void RemoveRule(string name)
    {
        lock (_lock) { _rules.RemoveAll(r => r.Name == name); }
    }

    public IReadOnlyList<TriggerRule> Rules
    {
        get { lock (_lock) { return _rules.ToList(); } }
    }

    public void Evaluate(LogEntry entry)
    {
        List<TriggerRule> snapshot;
        lock (_lock) { snapshot = _rules.Where(r => r.Enabled).ToList(); }

        foreach (var rule in snapshot)
        {
            if (MatchesRule(rule, entry))
            {
                OnTriggerFired?.Invoke(rule, entry);
            }
        }
    }

    private static bool MatchesRule(TriggerRule rule, LogEntry entry)
    {
        if (!string.IsNullOrEmpty(rule.Direction) &&
            !string.Equals(entry.Direction, rule.Direction, StringComparison.OrdinalIgnoreCase))
            return false;

        var target = rule.MatchHex ? entry.RawHex : entry.Text;
        if (string.IsNullOrEmpty(target)) return false;

        return rule.MatchMode.ToLowerInvariant() switch
        {
            "exact" => string.Equals(target, rule.Pattern, StringComparison.OrdinalIgnoreCase),
            "regex" => TryRegex(target, rule.Pattern),
            _ => target.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool TryRegex(string input, string pattern)
    {
        try { return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase); }
        catch (System.Text.RegularExpressions.RegexParseException) { return false; }
    }

    public static void SaveRules(IEnumerable<TriggerRule> rules, string filePath)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(rules.ToArray(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }

    public static List<TriggerRule> LoadRules(string filePath)
    {
        if (!File.Exists(filePath)) return new();
        var json = File.ReadAllText(filePath);
        return System.Text.Json.JsonSerializer.Deserialize<TriggerRule[]>(json)?.ToList() ?? new();
    }
}
