$ErrorActionPreference = "Stop"
$ProjectDir = $PSScriptRoot
$PidFile = Join-Path $ProjectDir ".acccom_gui.pid"

# Check PID file for existing instance
if (Test-Path $PidFile) {
    $oldPid = Get-Content $PidFile -Raw | ForEach-Object { $_.Trim() }
    if ($oldPid -match '^\d+$') {
        $proc = Get-Process -Id $oldPid -ErrorAction SilentlyContinue
        if ($proc) {
            Write-Host "[ACCcom] WPF already running (PID $oldPid)"
            exit 0
        }
    }
    Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
}

Write-Host "[ACCcom] Starting WPF desktop app..."
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = "dotnet"
$psi.Arguments = "run --project src\ACCcom\ACCcom.csproj -c Release"
$psi.WorkingDirectory = $ProjectDir
$psi.UseShellExecute = $true
$proc = [System.Diagnostics.Process]::Start($psi)

if ($proc) {
    $proc.Id | Set-Content $PidFile -NoNewline
    Write-Host "[ACCcom] Launched (PID $($proc.Id))"
}
