# apply-acccom-nogui-env.ps1
# 给 DSH 两个 profile 的 cordis.patch.yml 中 mcp-acccom 块追加 env: ACCCOM_MCP_NO_GUI=1
# (GuiNotifier 的 env 开关需先重新构建 ACCcom.McpServer 才生效)
# 幂等:已加过 / 找不到锚点都会安全跳过,不会重复插入、不会乱改。
$ErrorActionPreference = 'Stop'

$files = @(
    "$env:USERPROFILE\.dsh\profiles\desktop\cordis.patch.yml",
    "$env:USERPROFILE\.dsh\profiles\web\cordis.patch.yml"
)

$insert = @"

        # ACCCOM_MCP_NO_GUI=1: DSH 已装 dsh-serial-panel 面板(尾随同一份
        # mcp-traffic.jsonl),桌面 GUI 流量窗口是重复视图,禁掉自动拉起;
        # Claude Code 等无面板客户端不加此 env,保持自动弹窗行为。
        env:
          ACCCOM_MCP_NO_GUI: "1"
"@

foreach ($f in $files) {
    if (-not (Test-Path -LiteralPath $f)) { Write-Host "SKIP (文件不存在): $f"; continue }
    $text = [IO.File]::ReadAllText($f)
    if ($text -match 'ACCCOM_MCP_NO_GUI') { Write-Host "SKIP (已存在开关): $f"; continue }
    $pattern = '(?m)^([ \t]*command: D:/WORK_VSCODE/Vibe-coding/Xcom/src/ACCcom\.McpServer/bin/Release/net8\.0/ACCcom\.McpServer\.exe\r?\n[ \t]*args: \[\])'
    if ($text -notmatch $pattern) { Write-Host "ERROR (找不到 mcp-acccom 锚点,未改动): $f"; continue }
    $new = [regex]::Replace($text, $pattern, { param($m) $m.Groups[1].Value + $insert })
    [IO.File]::WriteAllText($f, $new)
    Write-Host "OK (已插入 env 开关): $f"
}

Write-Host ''
Write-Host '下一步:'
Write-Host '  1) 重新构建: dotnet build D:\WORK_VSCODE\Vibe-coding\Xcom\src\ACCcom.McpServer\ACCcom.McpServer.csproj -c Release'
Write-Host '  2) 完全退出并重启 DeepSeek Harness(loader 启动时读取 patch;MCP server 随会话重启)'
Write-Host '  3) 验证: 之后 AI 调用 open_port/send 时 ACCcom 桌面软件不再自动弹出;'
Write-Host '     DSH 侧边栏 ACCCOM 串口面板/traffic_log 数据不受影响。想手动开软件随时可以。'
