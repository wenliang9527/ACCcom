using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class MacroManager : JsonFilePersistenceManager<MacroTemplate>, IDisposable
{
    /// <summary>User macros live under LocalAppData so writes never hit a read-only install dir.</summary>
    public static readonly string MacrosFile = Path.Combine(BaseDir, "macros.json");
    protected override string FileName => "macros.json";

    private CancellationTokenSource? _cts;
    private bool _disposed;

    public async Task<bool> RunAsync(
        MacroTemplate macro,
        Action<string, bool> send,
        Func<string, string> expandVariables,
        Action<string> updateStatus,
        Func<string, string?>? findResponse = null)
    {
        var oldCts = _cts;
        _cts = new CancellationTokenSource();
        oldCts?.Dispose();
        var token = _cts.Token;

        string? lastResponse = null;

        try
        {
            for (int rep = 0; rep < macro.RepeatCount || macro.RepeatCount == 0; rep++)
            {
                token.ThrowIfCancellationRequested();

                for (int i = 0; i < macro.Steps.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var step = macro.Steps[i];

                    // Condition gates the step on the response of the previous wait:
                    // "contains:OK" sends only when the response contained OK;
                    // "notcontains:ERR" sends only when it did not.
                    if (step.Condition != null && !ConditionMet(step.Condition, lastResponse))
                    {
                        updateStatus($"Step {i + 1}/{macro.Steps.Count} skipped (condition) (round {rep + 1})");
                        continue;
                    }

                    if (step.DelayMs > 0)
                        await Task.Delay(step.DelayMs, token).ConfigureAwait(false);

                    var toSend = step.IsHex ? step.Command : expandVariables(step.Command);
                    send(toSend, step.IsHex);

                    updateStatus($"Step {i + 1}/{macro.Steps.Count} (round {rep + 1})");

                    if (step.WaitFor != null)
                        lastResponse = await WaitForResponseAsync(step.WaitFor, step.WaitTimeoutMs, token, findResponse).ConfigureAwait(false);
                    else if (i == 0)
                        lastResponse = null; // no previous response on the first step of a round
                }

                if (macro.RepeatDelayMs > 0 && (rep < macro.RepeatCount - 1 || macro.RepeatCount == 0))
                    await Task.Delay(macro.RepeatDelayMs, token).ConfigureAwait(false);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// Polls <paramref name="findResponse"/> for a recent RX text containing
    /// <paramref name="pattern"/> until it matches or the step times out.
    /// Returns the matching text, or null on timeout (which makes any pending
    /// "contains:" condition on the next step fail).
    /// </summary>
    private static async Task<string?> WaitForResponseAsync(
        string pattern,
        int timeoutMs,
        CancellationToken token,
        Func<string, string?>? findResponse)
    {
        if (findResponse == null)
        {
            // No response source wired (e.g. tests): fall back to the old
            // bounded pause (max 1s) so macros still advance.
            await Task.Delay(Math.Min(timeoutMs, 1000), token).ConfigureAwait(false);
            return null;
        }

        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < Math.Max(timeoutMs, 0))
        {
            token.ThrowIfCancellationRequested();
            var text = findResponse(pattern);
            if (text != null) return text;
            await Task.Delay(50, token).ConfigureAwait(false);
        }
        return null;
    }

    /// <summary>Evaluates a step condition ("contains:…" / "notcontains:…"). Unknown formats never skip.</summary>
    internal static bool ConditionMet(string condition, string? lastResponse)
    {
        const StringComparison cmp = StringComparison.OrdinalIgnoreCase;
        if (condition.StartsWith("contains:", cmp))
            return lastResponse != null && lastResponse.Contains(condition["contains:".Length..], cmp);
        if (condition.StartsWith("notcontains:", cmp))
            return lastResponse == null || !lastResponse.Contains(condition["notcontains:".Length..], cmp);
        return true;
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    public bool IsRunning => _cts != null;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
