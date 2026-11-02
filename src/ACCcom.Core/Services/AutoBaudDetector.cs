using System.IO.Ports;

namespace ACCcom.Core.Services;

public class AutoBaudDetector : IDisposable
{
    // Standard baud rates to try, in order of likelihood. Exposed so the UI
    // can show "trying 9600…" without re-declaring the list.
    public static readonly int[] CommonRates = { 9600, 115200, 57600, 38400, 19200, 4800, 2400, 1200, 460800, 230400, 921600 };

    private enum ProbeOutcome { Responded, NoResponse, PortUnavailable }

    /// <summary>Why the most recent DetectAsync returned 0, when it was not a
    /// plain "no device answered": access denied (port held by another
    /// process) or unavailable (unplugged mid-scan). Null after a successful
    /// detection or a clean full-scan miss. Detection used to collapse all
    /// three into silence — a port held by the GUI session looked identical
    /// to a device that simply did not respond.</summary>
    public string? LastDiagnostic { get; private set; }

    /// <summary>
    /// Auto-detect the baud rate of a connected serial device by probing common rates.
    /// Returns the detected baud rate, or 0 if not found.
    /// </summary>
    public async Task<int> DetectAsync(string portName, CancellationToken ct = default)
    {
        // A null/empty port name cannot be probed; fail as "no baud found"
        // instead of letting new SerialPort throw a low-level ArgumentException.
        if (string.IsNullOrEmpty(portName)) return 0;

        LastDiagnostic = null;
        foreach (var baudRate in CommonRates)
        {
            ct.ThrowIfCancellationRequested();
            var outcome = await ProbeAsync(portName, baudRate, ct).ConfigureAwait(false);
            if (outcome == ProbeOutcome.Responded)
                return baudRate;

            if (outcome == ProbeOutcome.PortUnavailable)
            {
                // Opening is baud-INDEPENDENT: if the port cannot be opened
                // now, the remaining rates would each repeat the same failure
                // (and on Windows every open/close cycle can take seconds).
                // Stop scanning and leave the diagnosis in LastDiagnostic.
                return 0;
            }
        }
        return 0;
    }

    /// <summary>
    /// Try a specific baud rate by opening the port, sending probe bytes, and checking for a response.
    /// </summary>
    public async Task<bool> TryBaudRateAsync(string portName, int baudRate, CancellationToken ct = default)
    {
        // Same contract as DetectAsync: a null/empty port name is "not found".
        if (string.IsNullOrEmpty(portName)) return false;
        return await ProbeAsync(portName, baudRate, ct).ConfigureAwait(false) == ProbeOutcome.Responded;
    }

    /// <summary>Open the port at one rate, send the probe, and see whether
    /// anything answers. Distinguishes "the port itself is not openable" from
    /// "opened fine but nothing replied" so the scanner can stop early.</summary>
    private async Task<ProbeOutcome> ProbeAsync(string portName, int baudRate, CancellationToken ct)
    {
        SerialPort? port = null;
        try
        {
            port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 200,
                WriteTimeout = 200,
                DtrEnable = false,
                RtsEnable = false
            };

            port.Open();

            // Send 0x00 three times with small delays to trigger a response
            for (int i = 0; i < 3; i++)
            {
                ct.ThrowIfCancellationRequested();
                port.Write(new byte[] { 0x00 }, 0, 1);
                await Task.Delay(50, ct).ConfigureAwait(false);
            }

            // Wait for any response
            await Task.Delay(200, ct).ConfigureAwait(false);

            return port.BytesToRead > 0 ? ProbeOutcome.Responded : ProbeOutcome.NoResponse;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
        {
            // Port held by another process (access denied), device gone /
            // nonexistent port (IOException), or an invalid port name
            // (ArgumentException). None of these depend on the baud rate.
            LastDiagnostic = ex is UnauthorizedAccessException
                ? $"Port {portName} is busy or access denied — close the other session first"
                : $"Port {portName} is unavailable: {ex.Message}";
            return ProbeOutcome.PortUnavailable;
        }
        catch
        {
            // Timeout / malformed low-level failures at this rate: keep scanning.
            return ProbeOutcome.NoResponse;
        }
        finally
        {
            if (port != null)
            {
                try
                {
                    if (port.IsOpen) port.Close();
                    port.Dispose();
                }
                catch
                {
                    // Cleanup failure is non-fatal
                }
            }
        }
    }

    public void Dispose()
    {
        // No persistent resources to dispose; each probe owns its port lifetime
    }
}
