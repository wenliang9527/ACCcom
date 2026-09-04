$ErrorActionPreference = "Stop"
$ProjectDir = $PSScriptRoot
Set-Location $ProjectDir

$ExePath = Join-Path $ProjectDir "src\ACCcom.McpServer\bin\Release\net8.0\ACCcom.McpServer.exe"
$ParserDir = "src/ACCcom.Core/parsers"

# Prefer the compiled exe: ZCode spawns the MCP server at session start,
# and `dotnet run` adds ~5-10s build/startup latency per connection.
if (Test-Path $ExePath) {
    & $ExePath --parsers-dir $ParserDir
    exit $LASTEXITCODE
}

Write-Host "[ACCcom] Release exe not found, falling back to dotnet run..." -ForegroundColor Yellow
dotnet run --project src\ACCcom.McpServer\ACCcom.McpServer.csproj -c Release --parsers-dir $ParserDir
