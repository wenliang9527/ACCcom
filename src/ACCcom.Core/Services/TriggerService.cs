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
        return PatternMatcher.Matches(entry, rule.Pattern, rule.MatchMode, rule.MatchHex, rule.Direction);
    }

    private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public static void SaveRules(IEnumerable<TriggerRule> rules, string filePath)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(rules.ToArray(), _jsonOptions);
        File.WriteAllText(filePath, json);
    }

    public static List<TriggerRule> LoadRules(string filePath)
    {
        if (!File.Exists(filePath)) return new();
        var json = File.ReadAllText(filePath);
        return System.Text.Json.JsonSerializer.Deserialize<TriggerRule[]>(json)?.ToList() ?? new();
    }
}
