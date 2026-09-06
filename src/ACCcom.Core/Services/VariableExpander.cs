namespace ACCcom.Core.Services;

/// <summary>
/// Expands {{variable}} placeholders in send payloads: timestamp/date/time,
/// a monotonically increasing {{counter}}, and {{ticks}}. Extracted from
/// DataFlowViewModel so the expansion table and the counter's stateful
/// behavior are unit-testable without a UI layer. The timestamp components
/// are injectable for deterministic tests; production passes DateTime.Now.
/// </summary>
public class VariableExpander
{
    private int _counter;

    /// <summary>Creates an expander. <paramref name="now"/> supplies the clock;
    /// omit for production (defaults to DateTime.Now per call).</summary>
    public VariableExpander(Func<DateTime>? now = null)
    {
        _now = now ?? (() => DateTime.Now);
    }

    private readonly Func<DateTime> _now;

    /// <summary>Expands {{timestamp}}, {{date}}, {{time}}, {{counter}} and
    /// {{ticks}} in <paramref name="input"/>. Inputs without a placeholder are
    /// returned unchanged (and the counter is not advanced).</summary>
    public string Expand(string input)
    {
        if (string.IsNullOrEmpty(input) || !input.Contains("{{", StringComparison.Ordinal))
            return input;

        var now = _now();
        return input
            .Replace("{{timestamp}}", now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Replace("{{date}}", now.ToString("yyyy-MM-dd"))
            .Replace("{{time}}", now.ToString("HH:mm:ss"))
            .Replace("{{counter}}", (++_counter).ToString())
            .Replace("{{ticks}}", now.Ticks.ToString());
    }

    /// <summary>Current counter value (how many {{counter}} expansions have
    /// happened). Exposed for observability/tests.</summary>
    public int Counter => _counter;
}
