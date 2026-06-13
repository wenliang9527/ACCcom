using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class MacroManager : JsonFilePersistenceManager<MacroTemplate>, IDisposable
{
    public static readonly string MacrosFile = Path.Combine(AppContext.BaseDirectory, "macros.json");
    protected override string FileName => "macros.json";

    private CancellationTokenSource? _cts;
    private bool _disposed;

    public async Task<bool> RunAsync(
        MacroTemplate macro,
        Action<string, bool> send,
        Func<string, string> expandVariables,
        Action<string> updateStatus)
    {
        var oldCts = _cts;
        _cts = new CancellationTokenSource();
        oldCts?.Dispose();
        var token = _cts.Token;

        try
        {
            for (int rep = 0; rep < macro.RepeatCount || macro.RepeatCount == 0; rep++)
            {
                token.ThrowIfCancellationRequested();

                for (int i = 0; i < macro.Steps.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var step = macro.Steps[i];

                    if (step.DelayMs > 0)
                        await Task.Delay(step.DelayMs, token);

                    var toSend = step.IsHex ? step.Command : expandVariables(step.Command);
                    send(toSend, step.IsHex);

                    updateStatus($"步骤 {i + 1}/{macro.Steps.Count} (第{rep + 1}轮)");

                    if (step.WaitFor != null)
                    {
                        await Task.Delay(Math.Min(step.WaitTimeoutMs, 1000), token);
                    }
                }

                if (macro.RepeatDelayMs > 0 && (rep < macro.RepeatCount - 1 || macro.RepeatCount == 0))
                    await Task.Delay(macro.RepeatDelayMs, token);
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
