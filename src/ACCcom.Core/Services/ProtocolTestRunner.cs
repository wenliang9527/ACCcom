using System.Text.Json;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

/// <summary>
/// Runs protocol test scripts against a serial device via callbacks,
/// producing structured pass/fail reports.
/// </summary>
public class ProtocolTestRunner
{
    private CancellationTokenSource? _cts;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Runs a test script. Each step sends a command and optionally verifies the response.
    /// </summary>
    /// <param name="script">The test script to execute.</param>
    /// <param name="send">Callback: (command, isHex) sends data over serial.</param>
    /// <param name="waitForResponse">Callback: (pattern, matchMode, matchHex, timeoutMs, ct) returns matched text or null.</param>
    /// <param name="ct">Optional external cancellation token.</param>
    /// <returns>A TestReport with per-step results.</returns>
    public async Task<TestReport> RunAsync(
        TestScript script,
        Action<string, bool> send,
        Func<string, string, bool, int, CancellationToken, Task<string?>> waitForResponse,
        CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;

        var report = new TestReport
        {
            ScriptName = script.Name,
            StartTime = DateTime.UtcNow
        };

        try
        {
            for (int rep = 0; rep < script.RepeatCount || script.RepeatCount == 0; rep++)
            {
                token.ThrowIfCancellationRequested();

                for (int i = 0; i < script.Steps.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var step = script.Steps[i];
                    var result = await ExecuteStepAsync(step, send, waitForResponse, token);
                    report.Results.Add(result);
                }

                if (script.RepeatDelayMs > 0 && (rep < script.RepeatCount - 1 || script.RepeatCount == 0))
                    await Task.Delay(script.RepeatDelayMs, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Execution was cancelled; report whatever results we have so far.
        }
        finally
        {
            report.EndTime = DateTime.UtcNow;
            _cts?.Dispose();
            _cts = null;
        }

        return report;
    }

    /// <summary>
    /// Stops a running test script.
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
    }

    /// <summary>
    /// Whether a test is currently running.
    /// </summary>
    public bool IsRunning => _cts != null;

    private async Task<TestStepResult> ExecuteStepAsync(
        TestStep step,
        Action<string, bool> send,
        Func<string, string, bool, int, CancellationToken, Task<string?>> waitForResponse,
        CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new TestStepResult
        {
            StepName = step.Name,
            ExpectedPattern = step.ExpectedPattern
        };

        try
        {
            // Apply pre-step delay
            if (step.DelayMs > 0)
                await Task.Delay(step.DelayMs, ct);

            int maxAttempts = 1 + step.RetryCount;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                result.Attempts = attempt;

                // Send the command
                send(step.Command, step.IsHex);

                // If no expected pattern, this is a send-only step
                if (string.IsNullOrEmpty(step.ExpectedPattern))
                {
                    result.Passed = true;
                    break;
                }

                // Wait for response
                var response = await waitForResponse(
                    step.ExpectedPattern,
                    step.MatchMode,
                    step.IsHex,
                    step.ResponseTimeoutMs,
                    ct);

                result.ActualResponse = response;

                if (response == null)
                {
                    result.Passed = false;
                    result.FailureReason = $"Timeout: no response matching '{step.ExpectedPattern}' within {step.ResponseTimeoutMs}ms";
                }
                else if (MatchesExpectation(response, step.ExpectedPattern, step.MatchMode))
                {
                    result.Passed = true;
                    result.FailureReason = null;
                }
                else
                {
                    result.Passed = false;
                    result.FailureReason = $"Mismatch: expected '{step.ExpectedPattern}' ({step.MatchMode}), got '{response}'";
                }

                // If passed or no retries left, stop
                if (result.Passed || attempt >= maxAttempts)
                    break;

                // Retry delay
                if (step.RetryDelayMs > 0)
                    await Task.Delay(step.RetryDelayMs, ct);
            }
        }
        catch (OperationCanceledException)
        {
            result.Passed = false;
            result.FailureReason = "Cancelled";
            throw; // Re-throw so the caller can handle cancellation
        }
        catch (Exception ex)
        {
            result.Passed = false;
            result.FailureReason = $"Error: {ex.Message}";
        }
        finally
        {
            sw.Stop();
            result.Duration = sw.Elapsed;
        }

        return result;
    }

    /// <summary>
    /// Checks whether an actual response matches the expected pattern using the given match mode.
    /// </summary>
    private static bool MatchesExpectation(string actual, string pattern, string matchMode)
    {
        return matchMode.ToLowerInvariant() switch
        {
            "exact" => string.Equals(actual, pattern, StringComparison.Ordinal),
            "regex" => TryRegexMatch(actual, pattern),
            "hex_contains" => actual.Contains(pattern, StringComparison.OrdinalIgnoreCase),
            _ => actual.Contains(pattern, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool TryRegexMatch(string input, string pattern)
    {
        try
        {
            return System.Text.RegularExpressions.Regex.IsMatch(input, pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Saves a test script to a JSON file.
    /// </summary>
    public static void SaveScript(TestScript script, string filePath)
    {
        var json = JsonSerializer.Serialize(script, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Loads a test script from a JSON file.
    /// </summary>
    public static TestScript LoadScript(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<TestScript>(json) ?? new TestScript();
    }

    /// <summary>
    /// Saves a test report to a JSON file.
    /// </summary>
    public static void SaveReport(TestReport report, string filePath)
    {
        var json = JsonSerializer.Serialize(report, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
