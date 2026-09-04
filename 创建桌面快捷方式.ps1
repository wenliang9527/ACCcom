# 创建 ACCcom 桌面快捷方式（指向 Release 产物；缺失则先构建）
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root "src\ACCcom\bin\Release\net8.0-windows\ACCcom.exe"

if (-not (Test-Path $exe)) {
    Write-Host "[快捷方式] Release 产物不存在，先构建..."
    Push-Location $root
    dotnet build "src\ACCcom\ACCcom.csproj" -c Release
    Pop-Location
    if (-not (Test-Path $exe)) { Write-Error "构建失败"; exit 1 }
}

$ws = New-Object -ComObject WScript.Shell
$desktop = [Environment]::GetFolderPath("Desktop")
$lnk = $ws.CreateShortcut((Join-Path $desktop "ACCcom 串口调试助手.lnk"))
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = Split-Path $exe
$lnk.IconLocation = "$exe,0"
$lnk.Description = "ACCcom 串口调试助手"
$lnk.Save()
Write-Host "[快捷方式] 已创建: $desktop\ACCcom 串口调试助手.lnk"
