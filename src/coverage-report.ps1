$ErrorActionPreference = 'Continue'
[Environment]::SetEnvironmentVariable('DOTNET_SYSTEM_NET_HTTP_USESOCKETSHTTPHANDLER', '1', 'Process')
[Environment]::SetEnvironmentVariable('DOTNET_CLI_TELEMETRY_OPTOUT', '1', 'Process')
[Environment]::SetEnvironmentVariable('DOTNET_NOLOGO', 'true', 'Process')

# Issue1 修复：dotnet 从 PATH 解析，兼容跨平台
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source

# Issue2 修复：基于脚本目录定位测试项目，绑定当前用户机器
$testsRoot = Join-Path $PSScriptRoot 'feishu-doc-export.Tests'

# Issue3 修复：输出目录也用相对路径
$reportDir = Join-Path $testsRoot 'coveragereport'

# 1. 安装 ReportGenerator（net6 兼容版本）
& $dotnet tool install --global dotnet-reportgenerator-globaltool --version 5.3.8 --add-source https://api.nuget.org/v3/index.json

# 2. 找最新的覆盖率 XML
$xml = Get-ChildItem $testsRoot -Recurse -Filter coverage.cobertura.xml | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $xml) {
    Write-Host '未找到 coverage.cobertura.xml，请先运行 dotnet test --collect:"XPlat Code Coverage"'
    exit 1
}
Write-Host ('XML: ' + $xml.FullName)

# 3. 生成 HTML + 文本摘要报表
$rg = (Get-Command reportgenerator -ErrorAction Stop).Source
$arguments = @(
    "-reports:$($xml.FullName)"
    "-targetdir:$reportDir"
    '-reporttypes:Html;TextSummary'
    '-verbosity:Info'
)
& $rg @arguments
Write-Host ('ReportGenerator exit: ' + $LASTEXITCODE)
Write-Host ('报表已生成: ' + (Join-Path $reportDir 'index.html'))
