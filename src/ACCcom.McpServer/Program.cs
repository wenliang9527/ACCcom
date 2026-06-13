using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ACCcom.McpServer;
using ACCcom.McpServer.Tools;
using ACCcom.Core.Services;

// --- ACCCOM MCP Server ---
// Serial port debugging tool for AI clients via Model Context Protocol (stdio).
//   --proxy              : proxy all serial operations through ACCCOM WPF HTTP API (http://127.0.0.1:8899)
//   --proxy-url <url>    : custom HTTP API URL (default http://127.0.0.1:8899)

var builder = Host.CreateApplicationBuilder(args);

var useProxy = args.Contains("--proxy");
var proxyUrl = args.SkipWhile(a => a != "--proxy-url").Skip(1).FirstOrDefault() ?? HttpService.DefaultUrl;

if (useProxy)
{
    // Ensure WPF app is running before starting MCP server
    var healthUrl = proxyUrl.TrimEnd('/') + "/api/health";
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    try
    {
        var healthResp = await http.GetAsync(healthUrl);
        if (!healthResp.IsSuccessStatusCode) throw new Exception("health check failed");
    }
    catch
    {
        Console.Error.WriteLine("[proxy] ACCCOM WPF not detected, launching...");
        var wpfProject = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ACCcom", "ACCcom.csproj");
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet", $"run --project \"{wpfProject}\"")
        {
            UseShellExecute = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal,
            WorkingDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."))
        };
        System.Diagnostics.Process.Start(psi);

        // Wait for WPF to start up (up to 30s)
        Console.Error.Write("[proxy] Waiting for WPF to start...");
        var started = false;
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(1000);
            try
            {
                using var cts = new CancellationTokenSource(2000);
                var resp = await http.GetAsync(healthUrl, cts.Token);
                if (resp.IsSuccessStatusCode) { started = true; break; }
            }
            catch { }
            Console.Error.Write(".");
        }
        Console.Error.WriteLine(started ? " OK" : " FAILED - WPF may not have started");
    }

    builder.Services.AddSingleton<ProxyClient>(_ => new ProxyClient(proxyUrl));
    builder.Services.AddSingleton<ParserManager>(sp =>
    {
        var parserDir = args.SkipWhile(a => a != "--parsers-dir").Skip(1).FirstOrDefault();
        return new ParserManager(parserDir);
    });
    builder.Services.AddSingleton<SessionRecorder>();
}
else
{
    builder.Services.AddSingleton<SerialService>();
    builder.Services.AddSingleton<ParserManager>(sp =>
    {
        var parserDir = args.SkipWhile(a => a != "--parsers-dir").Skip(1).FirstOrDefault();
        return new ParserManager(parserDir);
    });
    builder.Services.AddSingleton<LoggerService>();
    builder.Services.AddSingleton<MultiPortService>();
    builder.Services.AddSingleton<AutoBaudDetector>();
    builder.Services.AddSingleton<SessionRecorder>();
}

// Shared context for all tool classes
builder.Services.AddSingleton<ToolContext>();

// Register MCP tools
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<SerialTools>()
    .WithTools<ParserTools>()
    .WithTools<RecordingTools>()
    .WithTools<AnalysisTools>();

var app = builder.Build();
await app.RunAsync();
