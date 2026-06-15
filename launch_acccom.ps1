$ErrorActionPreference = "Stop"
$ProjectDir = $PSScriptRoot

Write-Host "[ACCcom] Starting MCP Server (direct mode)..."
Set-Location $ProjectDir
dotnet run --project src\ACCcom.McpServer\ACCcom.McpServer.csproj -c Release --parsers-dir src/ACCcom.Core/parsers
