using System.IO.Ports;

namespace ACCcom.Core.Services;

public class AutoBaudDetector : IDisposable
{
    // Standard baud rates to try, in order of likelihood
    private static readonly int[] CommonRates = { 9600, 115200, 57600, 38400, 19200, 4800, 2400, 1200, 460800, 230400, 921600 };

    /// <summary>
    /// Auto-detect the baud rate of a connected serial device by probing common rates.
    /// Returns the detected baud rate, or 0 if not found.
    /// </summary>
    public async Task<int> DetectAsync(string portName, CancellationToken ct = default)
    {
        foreach (var baudRate in CommonRates)
        {
            ct.ThrowIfCancellationRequested();
            if (await TryBaudRateAsync(portName, baudRate, ct).ConfigureAwait(false))
                return baudRate;
        }
        return 0;
    }

    /// <summary>
    /// Try a specific baud rate by opening the port, sending probe bytes, and checking for a response.
    /// </summary>
    public async Task<bool> TryBaudRateAsync(string portName, int baudRate, CancellationToken ct = default)
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

            if (port.BytesToRead > 0)
                return true;

            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Port may be in use or unavailable at this baud rate
            return false;
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
