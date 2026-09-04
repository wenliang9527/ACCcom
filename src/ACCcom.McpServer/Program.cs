using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ACCcom.McpServer;
using ACCcom.McpServer.Tools;
using ACCcom.Core.Services;

// --- ACCCOM MCP Server ---
// Basic serial port debugging tool for AI clients via Model Context Protocol (stdio).

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<ISerialService, SerialService>();
builder.Services.AddSingleton<ToolContext>();

// Register MCP tools
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<SerialTools>();

var app = builder.Build();
await app.RunAsync().ConfigureAwait(false);
