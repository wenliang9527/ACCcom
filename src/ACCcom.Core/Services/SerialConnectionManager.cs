using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class SerialConnectionManager : IDisposable
{
    private DateTime? _connectionStartTime;
    private System.Timers.Timer? _durationTimer;
    private bool _disposed;

    /// <summary>
    /// Raised every second with the formatted duration string (hh:mm:ss).
    /// </summary>
    public event Action<string>? DurationChanged;

    /// <summary>
    /// Attempts to toggle the serial connection.
    /// If currently open, closes the port and stops duration tracking.
    /// If currently closed, opens the port with the given config.
    /// Returns true if the port is now open, false otherwise.
    /// </summary>
    public bool ToggleConnection(SerialService serial, SerialConfig? config, bool currentlyOpen)
    {
        if (currentlyOpen)
        {
            serial.Close();
            StopTracking();
            return false;
        }

        if (config == null) return false;
        serial.Open(config);
        if (serial.IsOpen)
        {
            StartTracking();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Starts tracking connection duration. Invokes DurationChanged every second.
    /// </summary>
    public void StartTracking()
    {
        StopTracking();
        _connectionStartTime = DateTime.Now;
        _durationTimer = new System.Timers.Timer(1000);
        _durationTimer.Elapsed += (_, _) =>
        {
            if (_connectionStartTime.HasValue)
            {
                var elapsed = DateTime.Now - _connectionStartTime.Value;
                DurationChanged?.Invoke(elapsed.ToString(@"hh\:mm\:ss"));
            }
        };
        _durationTimer.Start();
        DurationChanged?.Invoke("00:00:00");
    }

    /// <summary>
    /// Stops duration tracking and resets state.
    /// </summary>
    public void StopTracking()
    {
        _durationTimer?.Stop();
        _durationTimer?.Dispose();
        _durationTimer = null;
        _connectionStartTime = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopTracking();
    }
}
