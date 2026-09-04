$ErrorActionPreference = "Stop"
$ProjectDir = $PSScriptRoot
Set-Location $ProjectDir

$ParserDir = "src/ACCcom.Core/parsers"
$PublishExe = Join-Path $ProjectDir "src\ACCcom.McpServer\bin\Release\net8.0\win-x64\publish\ACCcom.McpServer.exe"
$BuildExe = Join-Path $ProjectDir "src\ACCcom.McpServer\bin\Release\net8.0\ACCcom.McpServer.exe"
$Csproj = Join-Path $ProjectDir "src\ACCcom.McpServer\ACCcom.McpServer.csproj"

# Prefer the R2R-published exe (cold start ~310ms vs ~520ms) when it is
# newer than the source; otherwise the plain build, else `dotnet run`.
if (Test-Path $PublishExe) {
    $publishTime = (Get-Item $PublishExe).LastWriteTime
    $srcTime = (Get-Item $Csproj).LastWriteTime
    if ($publishTime -ge $srcTime) {
        & $PublishExe --parsers-dir $ParserDir
        exit $LASTEXITCODE
    }
}

if (Test-Path $BuildExe) {
    & $BuildExe --parsers-dir $ParserDir
    exit $LASTEXITCODE
}

Write-Host "[ACCcom] Release exe not found, falling back to dotnet run..." -ForegroundColor Yellow
dotnet run --project src\ACCcom.McpServer\ACCcom.McpServer.csproj -c Release --parsers-dir $ParserDir
