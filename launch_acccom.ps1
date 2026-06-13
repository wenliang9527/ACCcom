$ErrorActionPreference = "Stop"
$ProjectDir = "D:\WORK_VSCODE\Vibe-coding\Xcom"

Write-Host "[ACCcom] Starting MCP Server (direct mode)..."
dotnet run --project src\ACCcom.McpServer\ACCcom.McpServer.csproj -c Release --parsers-dir src/ACCcom.Core/parsers
