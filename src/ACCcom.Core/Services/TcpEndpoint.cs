namespace ACCcom.Core.Services;

/// <summary>A serial-over-TCP endpoint (host + port). The normalized form
/// <c>tcp://host:port</c> is what <see cref="ISerialService.CurrentPort"/>
/// reports and what tool responses echo, so bare-form input is always
/// normalized to it at parse time.</summary>
public readonly record struct TcpEndpoint(string Host, int Port)
{
    public override string ToString() => $"tcp://{Host}:{Port}";

    /// <summary>Firmware-side cap of the windowsair UART bridge's baud
    /// command (its parser rejects ≥ 2000000). Kept here so the tool layer
    /// can reject doomed rates up front instead of sending a handshake the
    /// bridge silently drops.</summary>
    public const int MaxBridgeBaud = 1_999_999;

    /// <summary>Recognizes the two accepted endpoint spellings in a port-name
    /// argument: canonical <c>tcp://host:port</c> and bare <c>host:port</c>
    /// (hostname/IPv4 charset — Windows COM names never contain a colon, so
    /// the bare form cannot shadow a serial port). The port is required;
    /// IPv6 needs the bracketed canonical form. Everything else (COM3,
    /// tcp://host without a port, out-of-range ports) returns false and
    /// stays on the serial path.</summary>
    public static bool TryParse(string? portName, out TcpEndpoint endpoint)
    {
        endpoint = default;
        var s = portName?.Trim();
        if (string.IsNullOrEmpty(s)) return false;

        string hostPart;
        bool canonical;
        if (s.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
        {
            canonical = true;
            hostPart = s["tcp://".Length..];
        }
        else
        {
            // Bare form: host must be a plain hostname/IPv4, so the charset
            // check below (no colons, no slashes, no spaces) is what keeps
            // IPv6 and URL-ish strings out of this path.
            canonical = false;
            hostPart = s;
        }

        var lastColon = hostPart.LastIndexOf(':');
        if (lastColon <= 0) return false; // no port, or an empty host

        var host = hostPart[..lastColon];
        var portText = hostPart[(lastColon + 1)..];

        if (portText.Length is < 1 or > 5 || !portText.All(char.IsDigit)) return false;
        if (!int.TryParse(portText, out var port) || port is < 1 or > 65535) return false;

        if (host.StartsWith('['))
        {
            // Bracketed IPv6 ("[::1]:1234") — canonical form only, and the
            // brackets must actually wrap an IPv6 literal (inner colon).
            if (!canonical || !host.EndsWith(']') || host.Length < 4) return false;
            host = host[1..^1];
            if (!host.Contains(':')) return false;
        }
        else if (host.Contains(':'))
        {
            return false; // bare IPv6 or multi-colon residue — no unbracketed IPv6
        }

        // '@' rejects userinfo residue in the canonical form; the bare form's
        // charset check below is already stricter than this line.
        if (host.Length == 0 || host.Contains(' ') || host.Contains('/') || host.Contains('@')) return false;
        if (!canonical && !IsPlainHost(host)) return false;

        endpoint = new TcpEndpoint(host, port);
        return true;
    }

    private static bool IsPlainHost(string host) =>
        host.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
}
