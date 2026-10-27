using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using ACCcom.McpServer;
using ACCcom.McpServer.Tools;
using ACCcom.Core.Services;

// --- ACCCOM MCP Server ---
// Basic serial port debugging tool for AI clients via Model Context Protocol (stdio).

// When the AI actually starts using the serial tools, surface the traffic in
// the desktop GUI: launch the ACCcom.exe with --open-mcp-traffic (if it isn't
// already running) so the user can watch what the AI sends/receives in real
// time. Launching is tied to the first real serial communication (opening a
// port or sending data), not to the MCP server process starting up — the
// server is spawned by background jobs/tests that must stay GUI-free. This is
// a best-effort side effect; the MCP server must keep working standalone.
//
// Opt-out: when the hosting client already provides its own traffic view —
// e.g. DeepSeek Harness with the dsh-serial-panel plugin, whose browser
// panel tails the same mcp-traffic.jsonl — set ACCCOM_MCP_NO_GUI=1 in that
// client's MCP server env to suppress the pop-up. The shared log keeps
// flowing either way; only the launch is skipped. Clients without a panel
// (Claude Code et al.) leave the variable unset and keep the auto-launch.
GuiNotifier.Attach();

var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { Args = args });

// stdio transport owns stdout: every other output target is forbidden. The
// default host builder wires Console logging to stdout, and those info lines
// interleaved with JSON-RPC responses (observed live: 24 log lines around 2).
// Route whatever still logs to stderr only.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// Port-name dispatching: "COM3" → SerialService, "tcp://host:port" (or bare
// "host:port") → TcpSerialService (wireless-DAP UART bridge). The same
// dispatching type backs the default session and the multi-port factory, so
// both session kinds accept TCP endpoints.
builder.Services.AddSingleton<ISerialService, EndpointSerialService>();
// Multi-port service: each non-empty tag owns an independent ISerialService.
// The default single-port session (empty tag) is the ToolContext's Serial.
builder.Services.AddSingleton(new MultiPortService(() => new EndpointSerialService()));
builder.Services.AddSingleton<ToolContext>();

// Register MCP tools
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<SerialTools>()
    .WithTools<TrafficTools>();

var app = builder.Build();
try
{
    await app.RunAsync().ConfigureAwait(false);
}
finally
{
    // Stdio EOF ends RunAsync; disposing the host tears down the disposable
    // singletons (serial services) so open TCP sessions close with a drain +
    // FIN instead of waiting for the process-exit socket cleanup. The
    // single-client UART bridge treats a lingering connection as occupied —
    // an explicit close here frees the slot for the next client promptly.
    // (Sync Dispose: this host's IHost interface version predates
    // IAsyncDisposable; the exit path has nothing left to await anyway.)
    app.Dispose();
}

/// <summary>
/// Lazily launches the desktop GUI (with the MCP traffic window) on the first
/// real serial communication, instead of at server startup. The MCP server is
/// also spawned by test runners and background automation, which must not pop
/// up a window; only an actual tool call that opens a port or sends data
/// triggers the launch, and then only once per process.
/// </summary>
internal static class GuiNotifier
{
    private static readonly object _lock = new();
    private static bool _launched;
    private static bool _attached;

    public static void Attach()
    {
        lock (_lock)
        {
            if (_attached) return;
            _attached = true;
        }
        SerialTools.GuiRequested += OnGuiRequested;
    }

    /// <summary>Client opt-out for the GUI auto-launch, via the
    /// ACCCOM_MCP_NO_GUI environment variable (accepts 1/true/yes/on,
    /// case-insensitive; anything else including unset means "launch").
    /// Used by clients that already surface the traffic themselves — the
    /// DeepSeek Harness dsh-serial-panel's browser panel is fed by the same
    /// mcp-traffic.jsonl, so the desktop pop-up would be a redundant second
    /// view. The MCP server's serial behavior is identical either way.</summary>
    private static bool GuiSuppressed()
    {
        var raw = Environment.GetEnvironmentVariable("ACCCOM_MCP_NO_GUI")?.Trim();
        if (string.IsNullOrEmpty(raw)) return false;
        return raw.Equals("1", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    public static void Request()
    {
        if (_launched) return;
        // Checked before arming the one-shot flag: a suppressed launch must
        // not consume the flag, and re-checking per call costs nothing (env
        // is stable for the process lifetime). stderr only — stdout is the
        // stdio transport's.
        if (GuiSuppressed())
        {
            Console.Error.WriteLine("[acccom-mcp] ACCCOM_MCP_NO_GUI is set: not launching the ACCcom desktop GUI; the shared mcp-traffic.jsonl keeps flowing (dsh-serial-panel or traffic_log still show the traffic).");
            return;
        }
        lock (_lock)
        {
            if (_launched) return;
            _launched = true;
        }
        TryLaunchGuiWithTrafficWindow();
    }

    private static void OnGuiRequested() => Request();

    private static void TryLaunchGuiWithTrafficWindow()
    {
        try
        {
            // Prefer a sibling ACCcom.exe; search up from this assembly's directory
            // through the repo layout (src/ACCcom.McpServer/bin/... -> src/ACCcom/...).
            var exe = FindGuiExe(AppContext.BaseDirectory);
            if (exe == null) return;

            // Don't spawn a second copy if the GUI is already running — the existing
            // instance would show the traffic window anyway via its own watcher.
            if (IsGuiRunning()) return;

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--open-mcp-traffic",
                UseShellExecute = true
            });
        }
        catch
        {
            // Launching the GUI is a nicety; never take the MCP server down for it.
        }
    }

    private static string? FindGuiExe(string startDir)
    {
        // Look for src/ACCcom/bin/{Config}/net8.0-windows/ACCcom.exe walking up
        // from the MCP server's output directory to the repo root.
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            foreach (var config in new[] { "Release", "Debug" })
            {
                var candidate = Path.Combine(dir.FullName, "src", "ACCcom", "bin", config, "net8.0-windows", "ACCcom.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }

    private static bool IsGuiRunning()
    {
        try
        {
            return Process.GetProcessesByName("ACCcom").Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
