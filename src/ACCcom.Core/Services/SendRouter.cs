namespace ACCcom.Core.Services;

/// <summary>
/// Single send funnel for every GUI-originated send: routes through the
/// network bridge while it is connected (bridge mode — the serial port is
/// idle), and falls back to the serial port otherwise. Both paths raise their
/// own TX <see cref="Models.LogEntry"/> through OnDataReceived, so statistics
/// and logging see exactly one entry per send either way — callers must not
/// add manual TX accounting on top.
///
/// Extracted because six call sites each carried (or, for trigger/macro/
/// protocol-test sends, silently lacked) the same bridge-or-serial branch.
/// </summary>
public static class SendRouter
{
    /// <summary>Synchronous send for background callers (trigger drain thread,
    /// loop timer, macro steps, protocol-test callbacks).</summary>
    public static bool Send(ISerialService serial, NetworkBridgeService? bridge, string? data, bool isHex)
        => bridge?.IsConnected == true
            ? bridge.Send(data, isHex)
            : serial.Send(data!, isHex);

    /// <summary>Async twin for UI-thread callers: the serial fallback goes
    /// through <see cref="ISerialService.SendAsync"/> so its retry sleeps do
    /// not freeze the caller. The bridge path is inherently non-blocking.</summary>
    public static async Task<bool> SendAsync(ISerialService serial, NetworkBridgeService? bridge, string? data, bool isHex)
        => bridge?.IsConnected == true
            ? bridge.Send(data, isHex)
            : await serial.SendAsync(data, isHex).ConfigureAwait(false);
}
