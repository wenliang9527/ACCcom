namespace ACCcom.Core.Models;

/// <summary>
/// A protocol test script containing ordered test steps with pass/fail assertions.
/// </summary>
public class TestScript
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<TestStep> Steps { get; set; } = new();
    public int RepeatCount { get; set; } = 1; // 0 = infinite
    public int RepeatDelayMs { get; set; } = 0;
}

/// <summary>
/// A single test step: send a command and verify the response.
/// </summary>
public class TestStep
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public bool IsHex { get; set; }
    public int DelayMs { get; set; } = 0;

    /// <summary>Expected response pattern. Null means send-only (no assertion).</summary>
    public string? ExpectedPattern { get; set; }

    /// <summary>Match mode: "contains", "exact", "regex", "hex_contains".</summary>
    public string MatchMode { get; set; } = "contains";

    /// <summary>Maximum time to wait for expected response in ms.</summary>
    public int ResponseTimeoutMs { get; set; } = 3000;

    /// <summary>Number of retry attempts if assertion fails.</summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>Delay between retries in ms.</summary>
    public int RetryDelayMs { get; set; } = 1000;
}

/// <summary>
/// Result of a single test step execution.
/// </summary>
public class TestStepResult
{
    public string StepName { get; set; } = "";
    public bool Passed { get; set; }
    public string? ActualResponse { get; set; }
    public string? ExpectedPattern { get; set; }
    public string? FailureReason { get; set; }
    public int Attempts { get; set; }
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Result of a complete test script execution.
/// </summary>
public class TestReport
{
    public string ScriptName { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public List<TestStepResult> Results { get; set; } = new();
    public int Passed => Results.Count(r => r.Passed);
    public int Failed => Results.Count(r => !r.Passed);
    public int Total => Results.Count;
    public bool AllPassed => Results.All(r => r.Passed);
    public TimeSpan Duration => EndTime - StartTime;
}
