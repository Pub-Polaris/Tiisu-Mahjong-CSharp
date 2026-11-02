# 规则库产物：dist/lib（Tiisu.Core.dll + Tiisu.Engine.dll + pdb + xml）与 zip
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File NET\tools\build_rules_lib.ps1
#   powershell -ExecutionPolicy Bypass -File NET\tools\build_rules_lib.ps1 -NoZip
#
# 说明：
#   - 只构建 Core / Engine（不碰 WinForms/Skia），产物可直接给别的宿主引用
#   - 版本号 = yymmdd-<git short hash>，写进 InformationalVersion 与 rules-version.json
#   - 不做 NuGet（2026-11-02 决定：保持现状，用 zip / 目录分发）

param(
    [string]$Configuration = 'Release',
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'

$netRoot = Split-Path -Parent $PSScriptRoot      # ...\NET
$dist = Join-Path $netRoot 'dist'
$lib = Join-Path $dist 'lib'

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$shortHash = (git -C $netRoot rev-parse --short HEAD 2>$null)
if (-not $shortHash) { $shortHash = 'local' }
$versionTag = (Get-Date).ToString('yyMMdd') + '-' + $shortHash
# 程序集版本必须是合法数字段（每段 <= 65535），所以用 年.月日.0
$numeric = (Get-Date).ToString('yy') + '.' + (Get-Date).ToString('MMdd') + '.0'

Write-Host "== build rules lib ($versionTag) =="

Push-Location $netRoot
try {
    dotnet build 'src\Tiisu.Core\Tiisu.Core.csproj' -c $Configuration -v q --nologo "/p:InformationalVersion=$versionTag" "/p:AssemblyVersion=$numeric" "/p:FileVersion=$numeric"
    if ($LASTEXITCODE -ne 0) { throw "Core build failed ($LASTEXITCODE)" }

    dotnet build 'src\Tiisu.Engine\Tiisu.Engine.csproj' -c $Configuration -v q --nologo "/p:InformationalVersion=$versionTag" "/p:AssemblyVersion=$numeric" "/p:FileVersion=$numeric"
    if ($LASTEXITCODE -ne 0) { throw "Engine build failed ($LASTEXITCODE)" }
}
finally { Pop-Location }

$coreOut = Join-Path $netRoot "src\Tiisu.Core\bin\$Configuration\net10.0"
$engineOut = Join-Path $netRoot "src\Tiisu.Engine\bin\$Configuration\net10.0"

if (Test-Path $lib) { Remove-Item $lib -Recurse -Force }
New-Item -ItemType Directory -Force -Path $lib | Out-Null

$files = @(
    @{ From = $coreOut;   Name = 'Tiisu.Core.dll' },
    @{ From = $coreOut;   Name = 'Tiisu.Core.pdb' },
    @{ From = $coreOut;   Name = 'Tiisu.Core.xml' },
    @{ From = $engineOut; Name = 'Tiisu.Engine.dll' },
    @{ From = $engineOut; Name = 'Tiisu.Engine.pdb' },
    @{ From = $engineOut; Name = 'Tiisu.Engine.xml' }
)

foreach ($f in $files) {
    $src = Join-Path $f.From $f.Name
    if (-not (Test-Path $src)) { throw "missing build output: $src" }
    Copy-Item $src (Join-Path $lib $f.Name) -Force
}

# 版本清单：宿主启动时可以读它做兼容性检查
$manifest = @{
    version   = $versionTag
    buildTime = (Get-Date).ToString('s')
    assemblies = @(
        @{ name = 'Tiisu.Core';   file = 'Tiisu.Core.dll' },
        @{ name = 'Tiisu.Engine'; file = 'Tiisu.Engine.dll' }
    )
    apiDoc    = 'docs/DSH/规则库API_261102.md'
    runtime   = 'net10.0'
} | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText((Join-Path $lib 'rules-version.json'), $manifest, [System.Text.UTF8Encoding]::new($false))

$sizeMb = [math]::Round((Get-ChildItem $lib -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 2)
Write-Host "   lib: $lib ($sizeMb MB)"

if (-not $NoZip) {
    $zip = Join-Path $dist ("Tiisu.Rules." + $versionTag + ".zip")
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $lib '*') -DestinationPath $zip -CompressionLevel Optimal
    $zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 2)
    Write-Host "   zip: $zip ($zipMb MB)"
}

Write-Host ""
Write-Host "OK  rules lib: $lib"
Write-Host "    version: $versionTag"
Write-Host "    usage: 引用 Tiisu.Core.dll + Tiisu.Engine.dll；契约见 docs/DSH/规则库API_261102.md"
