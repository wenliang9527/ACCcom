using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ACCcom.McpServer;
using ACCcom.McpServer.Tools;
using ACCcom.Core.Services;

// --- ACCCOM MCP Server ---
// Basic serial port debugging tool for AI clients via Model Context Protocol (stdio).

// When the AI starts using the serial tools, surface the traffic in the desktop
// GUI: launch the ACCcom.exe with --open-mcp-traffic (if it isn't already
// running) so the user can watch what the AI sends/receives in real time. This
// is a best-effort side effect — the MCP server must keep working standalone.
TryLaunchGuiWithTrafficWindow();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<ISerialService, SerialService>();
builder.Services.AddSingleton<ToolContext>();

// Register MCP tools
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<SerialTools>();

var app = builder.Build();
await app.RunAsync().ConfigureAwait(false);

static void TryLaunchGuiWithTrafficWindow()
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

static string? FindGuiExe(string startDir)
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

static bool IsGuiRunning()
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
