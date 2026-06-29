# 从 .csx 解析器文件自动提取特征生成 .schema.json
param(
    [string]$ParserDir = "src\ACCcom\bin\Debug\net8.0-windows\parsers"
)

function Extract-HeaderPattern {
    param([string]$content)
    
    if ($content -match '0x([0-9A-Fa-f]{2})\s*&&\s*RawData\[1\]\s*==\s*0x([0-9A-Fa-f]{2})') {
        return "$($matches[1]) $($matches[2])"
    }
    if ($content -match 'header\s*==\s*0x([0-9A-Fa-f]{2})([0-9A-Fa-f]{2})') {
        return "$($matches[1]) $($matches[2])"
    }
    if ($content -match 'ToUInt16\(0.*?\)\s*==\s*0x([0-9A-Fa-f]{4})') {
        $hex = $matches[1]
        return "$($hex.Substring(0,2)) $($hex.Substring(2,2))"
    }
    return $null
}

function Extract-MinLength {
    param([string]$content)
    
    if ($content -match 'RawData\.Length\s*<\s*(\d+)') {
        return [int]$matches[1]
    }
    return 0
}

$parsers = Get-ChildItem -Path $ParserDir -Filter "*.csx"

foreach ($parser in $parsers) {
    $schemaPath = Join-Path $ParserDir ($parser.BaseName + ".schema.json")
    
    if (Test-Path $schemaPath) {
        Write-Host "Skip $($parser.Name) - schema exists"
        continue
    }
    
    $content = Get-Content $parser.FullName -Raw
    
    $header = Extract-HeaderPattern $content
    $minLength = Extract-MinLength $content
    
    $schema = @{
        name = $parser.BaseName
        description = "$($parser.BaseName) protocol parser"
        minLength = $minLength
    }
    
    if ($header) {
        $schema.autoMatch = @{
            enabled = $true
            priority = 5
            headerPattern = $header
        }
        $schema.frame = @{
            header = $header
        }
    }
    
    $schema | ConvertTo-Json -Depth 10 | Set-Content -Path $schemaPath -Encoding UTF8
    Write-Host "Generated: $schemaPath"
    if ($header) {
        Write-Host "  Header: $header"
    }
}

Write-Host "Done!"
