# 将 ACCcom.McpServer 发布到 %LOCALAPPDATA%\ACCcom\mcp-server —— 一个任何构建、
# git clean、发布脚本都不会触碰的稳定位置，供 MCP 客户端（.claude/settings.json、
# DSH profiles）常驻挂载。客户端直接挂 bin\* 会在构建时锁住输出文件，dotnet build
# 反复撞 MSB3027/3021（2026-10-24 审计轮一天撞 4 次的根因）。
#
# 注意：改动 src/ACCcom.McpServer 后必须重跑本脚本，否则 MCP 客户端连到的仍是
# 旧服务器（schema/行为漂移会造成难查的调试假象）。见 CONTRIBUTING.md。
param(
    [string]$OutputDir = "$env:LOCALAPPDATA\ACCcom\mcp-server"
)

$ErrorActionPreference = 'Stop'

# 先停掉可能在跑的 MCP 服务器实例：从 bin\* 挂载的会锁住构建输出，从本目录挂载的
# 会锁住发布覆盖。客户端会在下次调用时按配置重新拉起。
Get-Process ACCcom.McpServer -ErrorAction SilentlyContinue | Stop-Process -Force

$repoRoot = Split-Path -Parent $PSScriptRoot
dotnet publish (Join-Path $repoRoot 'src\ACCcom.McpServer\ACCcom.McpServer.csproj') -c Release -o $OutputDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

$exe = Join-Path $OutputDir 'ACCcom.McpServer.exe'
if (-not (Test-Path $exe)) { throw "publish reported success but $exe is missing" }

Write-Host "published: $exe"
Write-Host "MCP client config command -> $exe"
