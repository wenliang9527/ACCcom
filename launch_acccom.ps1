param(
    [switch]$NoProxy
)

$ErrorActionPreference = "Stop"
$ProjectDir = "D:\WORK_VSCODE\Vibe-coding\Xcom"

# Step 1: Launch WPF Desktop App (if not running)
$wpf = Get-Process ACCcom -ErrorAction SilentlyContinue
if (-not $wpf) {
    Write-Host "[ACCcom] Starting WPF desktop app..."
    Start-Process dotnet -ArgumentList "run", "--project", "src\ACCcom\ACCcom.csproj", "-c", "Release" -WorkingDirectory $ProjectDir
    Start-Sleep 3

    $retry = 0
    while ($retry -lt 10) {
        try {
            $r = Invoke-WebRequest -Uri "http://127.0.0.1:8899/api/health" -UseBasicParsing -ErrorAction Stop
            if ($r.Content -match "ok") {
                Write-Host "[ACCcom] WPF API ready on port 8899"
                break
            }
        } catch {}
        Start-Sleep 1
        $retry++
    }
    if ($retry -ge 10) {
        Write-Warning "[ACCcom] WPF API did not respond in time, continuing anyway..."
    }
} else {
    Write-Host "[ACCcom] WPF already running (PID $($wpf.Id))"
}

# Step 2: Launch MCP Server
if ($NoProxy) {
    Write-Host "[ACCcom] Starting MCP Server (direct mode)..."
    dotnet run --project src\ACCcom.McpServer\ACCcom.McpServer.csproj -c Release --parsers-dir src/ACCcom.Core/parsers
} else {
    Write-Host "[ACCcom] Starting MCP Server (proxy mode -> WPF)..."
    dotnet run --project src\ACCcom.McpServer\ACCcom.McpServer.csproj -c Release --parsers-dir src/ACCcom.Core/parsers --proxy
}
