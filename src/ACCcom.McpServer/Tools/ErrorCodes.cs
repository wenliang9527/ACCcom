namespace ACCcom.McpServer.Tools;

/// <summary>
/// Stable machine-readable error codes for the failure envelope
/// {"success":false,"error":{"code","message"}}. Codes are part of the MCP
/// contract: agents branch on <c>code</c>, humans read <c>message</c>. Never
/// renumber or rename an existing code — add new ones instead.
/// </summary>
internal static class ErrorCodes
{
    /// <summary>Required port name argument was empty.</summary>
    public const string PortRequired = "PORT_REQUIRED";

    /// <summary>The tag (or default session) has no open port.</summary>
    public const string PortNotOpen = "PORT_NOT_OPEN";

    /// <summary>The OS refused to open the port (in use, missing, permissions).</summary>
    public const string OpenFailed = "OPEN_FAILED";

    /// <summary>Close reported failure.</summary>
    public const string CloseFailed = "CLOSE_FAILED";

    /// <summary>Send failed after validation (port closed mid-call).</summary>
    public const string SendFailed = "SEND_FAILED";

    /// <summary>data/payload argument was empty.</summary>
    public const string EmptyData = "EMPTY_DATA";

    /// <summary>isHex=true but the payload is not valid hex.</summary>
    public const string InvalidHex = "INVALID_HEX";

    /// <summary>pattern argument was empty.</summary>
    public const string PatternRequired = "PATTERN_REQUIRED";

    /// <summary>read_data fields selector contains an unknown column name.</summary>
    public const string InvalidFields = "INVALID_FIELDS";

    /// <summary>An argument value is out of its documented range (stopBits, parity, dataBits, baudRate).</summary>
    public const string InvalidConfig = "INVALID_CONFIG";

    /// <summary>matchMode is not contains/exact/regex, or a regex pattern failed to compile.</summary>
    public const string InvalidPattern = "INVALID_PATTERN";

    /// <summary>An unexpected exception escaped a tool body (message carries type + reason).</summary>
    public const string Internal = "INTERNAL";
}
