using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
GuiNotifier.Attach();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<ISerialService, SerialService>();
builder.Services.AddSingleton<ToolContext>();

// Register MCP tools
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<SerialTools>();

var app = builder.Build();
await app.RunAsync().ConfigureAwait(false);

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

    public static void Request()
    {
        if (_launched) return;
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
