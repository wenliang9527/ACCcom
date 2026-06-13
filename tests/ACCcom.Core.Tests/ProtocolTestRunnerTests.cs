using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class ProtocolTestRunnerTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            if (File.Exists(f)) File.Delete(f);
        }
    }

    private string TempFile(string ext = ".json")
    {
        var path = Path.Combine(Path.GetTempPath(), $"ptr_test_{Guid.NewGuid():N}{ext}");
        _tempFiles.Add(path);
        return path;
    }

    private static TestScript MakeScript(params TestStep[] steps) => new()
    {
        Name = "UnitTest",
        Description = "test script",
        Steps = new List<TestStep>(steps),
        RepeatCount = 1
    };

    private static TestStep SendOnly(string name = "Send", string cmd = "AT") => new()
    {
        Name = name,
        Command = cmd,
        IsHex = false
    };

    private static TestStep WithExpectation(
        string name, string cmd, string pattern, string mode = "contains",
        int timeoutMs = 3000, int retryCount = 0, int retryDelayMs = 0) => new()
    {
        Name = name,
        Command = cmd,
        IsHex = false,
        ExpectedPattern = pattern,
        MatchMode = mode,
        ResponseTimeoutMs = timeoutMs,
        RetryCount = retryCount,
        RetryDelayMs = retryDelayMs
    };

    // --- Mock helpers ---

    private static (List<(string cmd, bool hex)> sent, Action<string, bool> send) MakeSendMock()
    {
        var sent = new List<(string cmd, bool hex)>();
        return (sent, (cmd, hex) => sent.Add((cmd, hex)));
    }

    private static Func<string, string, bool, int, CancellationToken, Task<string?>> MakeWaitMock(string? response)
    {
        return async (pattern, mode, hex, timeout, ct) =>
        {
            await Task.Delay(10, ct);
            return response;
        };
    }

    private static Func<string, string, bool, int, CancellationToken, Task<string?>> MakeTimeoutWaitMock()
    {
        return (pattern, mode, hex, timeout, ct) => Task.FromResult<string?>(null);
    }

    // --- Tests ---

    [Fact]
    public async Task RunAsync_send_only_step_passes()
    {
        // Arrange
        var script = MakeScript(SendOnly());
        var (sent, send) = MakeSendMock();
        var wait = MakeWaitMock(null);
        var runner = new ProtocolTestRunner();

        // Act
        var report = await runner.RunAsync(script, send, wait);

        // Assert
        Assert.True(report.AllPassed);
        Assert.Single(report.Results);
        Assert.True(report.Results[0].Passed);
        Assert.Equal("AT", sent[0].cmd);
        Assert.False(sent[0].hex);
    }

    [Fact]
    public async Task RunAsync_matching_contains_passes()
    {
        // Arrange
        var step = WithExpectation("Contains", "AT+VER?", "OK");
        var script = MakeScript(step);
        var (_, send) = MakeSendMock();
        var wait = MakeWaitMock("AT+VER? OK v1.0");
        var runner = new ProtocolTestRunner();

        // Act
        var report = await runner.RunAsync(script, send, wait);

        // Assert
        Assert.True(report.AllPassed);
        Assert.Equal("AT+VER? OK v1.0", report.Results[0].ActualResponse);
    }

    [Fact]
    public async Task RunAsync_matching_exact_passes()
    {
        // Arrange
        var step = WithExpectation("Exact", "AT", "OK", mode: "exact");
        var script = MakeScript(step);
        var (_, send) = MakeSendMock();
        var wait = MakeWaitMock("OK");
        var runner = new ProtocolTestRunner();

        // Act
        var report = await runner.RunAsync(script, send, wait);

        // Assert
        Assert.True(report.AllPassed);
    }

    [Fact]
    public async Task RunAsync_matching_regex_passes()
    {
        // Arrange
        var step = WithExpectation("Regex", "AT+GMR", @"v\d+\.\d+", mode: "regex");
        var script = MakeScript(step);
        var (_, send) = MakeSendMock();
        var wait = MakeWaitMock("firmware v2.1.3");
        var runner = new ProtocolTestRunner();

        // Act
        var report = await runner.RunAsync(script, send, wait);

        // Assert
        Assert.True(report.AllPassed);
    }

    [Fact]
    public async Task RunAsync_mismatch_fails()
    {
        // Arrange
        var step = WithExpectation("Mismatch", "AT", "ERROR");
        var script = MakeScript(step);
        var (_, send) = MakeSendMock();
        var wait = MakeWaitMock("OK");
        var runner = new ProtocolTestRunner();

        // Act
        var report = await runner.RunAsync(script, send, wait);

        // Assert
        Assert.False(report.AllPassed);
        Assert.Contains("Mismatch", report.Results[0].FailureReason!);
    }

    [Fact]
    public async Task RunAsync_timeout_fails()
    {
        // Arrange
        var step = WithExpectation("Timeout", "AT", "OK", timeoutMs: 500);
        var script = MakeScript(step);
        var (_, send) = MakeSendMock();
        var wait = MakeTimeoutWaitMock();
        var runner = new ProtocolTestRunner();

        // Act
        var report = await runner.RunAsync(script, send, wait);

        // Assert
        Assert.False(report.Results[0].Passed);
        Assert.Contains("Timeout", report.Results[0].FailureReason!);
    }

    [Fact]
    public async Task RunAsync_retry_succeeds_on_second_attempt()
    {
        // Arrange
        var step = WithExpectation("Retry", "AT", "OK", retryCount: 2, retryDelayMs: 10);
        var script = MakeScript(step);
        var (_, send) = MakeSendMock();

        int callCount = 0;
        Func<string, string, bool, int, CancellationToken, Task<string?>> wait =
            async (pattern, mode, hex, timeout, ct) =>
            {
                await Task.Delay(5, ct);
                callCount++;
                return callCount >= 2 ? "OK" : "WRONG";
            };
        var runner = new ProtocolTestRunner();

        // Act
        var report = await runner.RunAsync(script, send, wait);

        // Assert
        Assert.True(report.AllPassed);
        Assert.Equal(2, report.Results[0].Attempts);
    }

    [Fact]
    public async Task RunAsync_retry_exhausted_fails()
    {
        // Arrange
        var step = WithExpectation("ExhaustRetry", "AT", "OK", retryCount: 2, retryDelayMs: 10);
        var script = MakeScript(step);
        var (_, send) = MakeSendMock();
        var wait = MakeWaitMock("WRONG");
        var runner = new ProtocolTestRunner();

        // Act
        var report = await runner.RunAsync(script, send, wait);

        // Assert
        Assert.False(report.Results[0].Passed);
        Assert.Equal(3, report.Results[0].Attempts); // 1 initial + 2 retries
    }

    [Fact]
    public async Task RunAsync_delay_applied_between_steps()
    {
        // Arrange
        var step1 = SendOnly("Step1", "AT");
        step1.DelayMs = 50;
        var step2 = SendOnly("Step2", "AT");
        step2.DelayMs = 50;
        var script = MakeScript(step1, step2);
        var (_, send) = MakeSendMock();
        var wait = MakeWaitMock(null);
        var runner = new ProtocolTestRunner();

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var report = await runner.RunAsync(script, send, wait);
        sw.Stop();

        // Assert
        Assert.True(report.AllPassed);
        Assert.Equal(2, report.Results.Count);
        // Two 50ms delays = at least 80ms total (allowing margin)
        Assert.True(sw.ElapsedMilliseconds >= 80,
            $"Expected >= 80ms but was {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task RunAsync_cancellation_stops_early()
    {
        // Arrange
        var steps = new List<TestStep>();
        for (int i = 0; i < 100; i++)
        {
            var s = SendOnly($"Step{i}", "AT");
            s.DelayMs = 10;
            steps.Add(s);
        }

        var script = new TestScript
        {
            Name = "Long",
            Steps = steps,
            RepeatCount = 1
        };

        var (_, send) = MakeSendMock();
        var wait = MakeWaitMock(null);
        var runner = new ProtocolTestRunner();
        using var cts = new CancellationTokenSource();

        // Cancel after a short delay
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        // Act
        var report = await runner.RunAsync(script, send, wait, cts.Token);

        // Assert
        Assert.True(report.Results.Count < 100, "Should have stopped early");
    }

    [Fact]
    public async Task RunAsync_multiple_steps_mixed_results()
    {
        // Arrange
        var step1 = SendOnly("Pass", "AT");
        var step2 = WithExpectation("Fail", "AT", "ERROR");
        var step3 = WithExpectation("Pass2", "AT", "OK");

        var script = MakeScript(step1, step2, step3);
        var (_, send) = MakeSendMock();

        int callCount = 0;
        Func<string, string, bool, int, CancellationToken, Task<string?>> wait =
            async (pattern, mode, hex, timeout, ct) =>
            {
                await Task.Delay(5, ct);
                callCount++;
                // First call (step2) returns wrong, second call (step3) returns matching
                return callCount == 1 ? "WRONG" : "AT OK ready";
            };
        var runner = new ProtocolTestRunner();

        // Act
        var report = await runner.RunAsync(script, send, wait);

        // Assert
        Assert.Equal(3, report.Results.Count);
        Assert.True(report.Results[0].Passed);   // send-only
        Assert.False(report.Results[1].Passed);  // mismatch
        Assert.True(report.Results[2].Passed);   // contains OK
        Assert.Equal(2, report.Passed);   // send-only + contains match
        Assert.Equal(1, report.Failed);   // mismatch
        Assert.False(report.AllPassed);
    }

    [Fact]
    public void SaveScript_and_LoadScript_roundtrip()
    {
        // Arrange
        var script = new TestScript
        {
            Name = "RoundTrip",
            Description = "test save/load",
            RepeatCount = 3,
            RepeatDelayMs = 100,
            Steps = new List<TestStep>
            {
                new() { Name = "Step1", Command = "AT", IsHex = false, ExpectedPattern = "OK", MatchMode = "contains" },
                new() { Name = "Step2", Command = "FF 01", IsHex = true, DelayMs = 50 }
            }
        };
        var path = TempFile();

        // Act
        ProtocolTestRunner.SaveScript(script, path);
        var loaded = ProtocolTestRunner.LoadScript(path);

        // Assert
        Assert.Equal("RoundTrip", loaded.Name);
        Assert.Equal("test save/load", loaded.Description);
        Assert.Equal(3, loaded.RepeatCount);
        Assert.Equal(100, loaded.RepeatDelayMs);
        Assert.Equal(2, loaded.Steps.Count);
        Assert.Equal("AT", loaded.Steps[0].Command);
        Assert.Equal("OK", loaded.Steps[0].ExpectedPattern);
        Assert.True(loaded.Steps[1].IsHex);
    }

    [Fact]
    public void SaveReport_writes_valid_json()
    {
        // Arrange
        var report = new TestReport
        {
            ScriptName = "ReportTest",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddSeconds(1),
            Results = new List<TestStepResult>
            {
                new() { StepName = "S1", Passed = true, Attempts = 1, Duration = TimeSpan.FromMilliseconds(50) },
                new() { StepName = "S2", Passed = false, FailureReason = "timeout", Attempts = 3, Duration = TimeSpan.FromSeconds(1) }
            }
        };
        var path = TempFile();

        // Act
        ProtocolTestRunner.SaveReport(report, path);

        // Assert
        var json = File.ReadAllText(path);
        Assert.Contains("\"ScriptName\": \"ReportTest\"", json);
        Assert.Contains("\"Passed\": true", json);
        Assert.Contains("\"Failed\": 1", json);

        // Verify it can be deserialized back
        var reloaded = System.Text.Json.JsonSerializer.Deserialize<TestReport>(json);
        Assert.NotNull(reloaded);
        Assert.Equal(2, reloaded!.Total);
        Assert.Equal(1, reloaded.Passed);
        Assert.Equal(1, reloaded.Failed);
        Assert.False(reloaded.AllPassed);
    }
}
