param(
  # 可选：直接传参覆盖凭证（优先级最高，适合 CI / 临时使用）
  [string]$AppId,
  [string]$AppSecret,
  [string]$SpaceId,
  # 可选：自定义配置文件路径
  [string]$ConfigPath = (Join-Path $PSScriptRoot 'config.json'),
  [string]$CredentialPath = (Join-Path $PSScriptRoot 'credentials.local.json'),
  # 仅校验配置并打印（脱敏）运行参数，不实际执行导出
  [switch]$DryRun
)

# 说明：exe 已原生支持 config.json / credentials.local.json（无参数运行即自动读取）。
# 本脚本保留用于：脱敏预检（-DryRun）、日志捕获（run.log/run.err）、以及免交互启动。

$ErrorActionPreference = 'Stop'

# ---------- 1. 读取基础配置（config.json 上库，凭证字段留空） ----------
if (-not (Test-Path $ConfigPath)) {
    throw "配置文件不存在: $ConfigPath"
}
$config = Get-Content $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json

# ---------- 2. 本地凭证覆盖（credentials.local.json 已 gitignore，不上库） ----------
if (Test-Path $CredentialPath) {
    $cred = Get-Content $CredentialPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($cred.appId)     { $config.appId     = $cred.appId }
    if ($cred.appSecret) { $config.appSecret = $cred.appSecret }
    if ($cred.spaceId)   { $config.spaceId   = $cred.spaceId }
}

# ---------- 3. 命令行参数覆盖（优先级最高） ----------
if ($AppId)     { $config.appId     = $AppId }
if ($AppSecret) { $config.appSecret = $AppSecret }
if ($SpaceId)   { $config.spaceId   = $SpaceId }

# ---------- 4. 缺失凭证时交互式输入 ----------
if (-not $config.appId) {
    $config.appId = Read-Host '请输入 AppId'
}
if (-not $config.appSecret) {
    $secure = Read-Host '请输入 AppSecret' -AsSecureString
    $config.appSecret = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}
if (-not $config.spaceId) {
    $config.spaceId = Read-Host '请输入 SpaceId'
}

# ---------- 5. 校验必填项 ----------
if (-not $config.spaceId)    { throw 'spaceId 未配置（config.json）' }
if (-not $config.exportPath) { throw 'exportPath 未配置（config.json）' }
if (-not $config.appId)      { throw 'appId 未配置' }
if (-not $config.appSecret)  { throw 'appSecret 未配置' }

# ---------- 6. 准备导出目录 ----------
if (-not (Test-Path $config.exportPath)) {
    New-Item -Path $config.exportPath -ItemType Directory -Force | Out-Null
}

# ---------- 7. 组装运行参数 ----------
$appArgs = @(
    ('--appId=' + $config.appId),
    ('--appSecret=' + $config.appSecret),
    ('--spaceId=' + $config.spaceId),
    ('--saveType=' + $config.saveType),
    ('--exportPath=' + $config.exportPath)
)
if ($config.quit) { $appArgs += '--quit' }
$appArgsStr = $appArgs -join ' '

# 打印时脱敏 appSecret 与 spaceId
$maskedArgs = $appArgsStr -replace [Regex]::Escape($config.appSecret), '****'
$maskedArgs = $maskedArgs -replace [Regex]::Escape($config.spaceId), '****'

$exe = Join-Path $PSScriptRoot 'dist\run\feishu-doc-export.exe'
$logPath = Join-Path $PSScriptRoot 'run.log'
$errPath = Join-Path $PSScriptRoot 'run.err'

if (-not (Test-Path $exe)) {
    throw "可执行文件不存在: $exe（请先执行 dotnet publish 生成）"
}

Write-Host ('Exe:  ' + $exe)
Write-Host ('Args: ' + $maskedArgs)

if ($DryRun) {
    Write-Host '[DryRun] 配置校验通过，未实际执行导出。'
    exit 0
}

# ---------- 8. 启动并捕获输出 ----------
$p = Start-Process -FilePath $exe `
    -ArgumentList $appArgsStr `
    -NoNewWindow `
    -RedirectStandardOutput $logPath `
    -RedirectStandardError $errPath `
    -PassThru `
    -Wait

Write-Host ('ExitCode: ' + $p.ExitCode)
Write-Host "----- STDOUT（run.log 末尾 30 行）-----"
if (Test-Path $logPath) { Get-Content $logPath -Tail 30 -Encoding UTF8 }
Write-Host "----- STDERR -----"
if (Test-Path $errPath) { Get-Content $errPath -Raw -Encoding UTF8 }
