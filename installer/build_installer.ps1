# Build the NSIS installer for Tiisu Mahjong (PowerShell 5.1)
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File NET\installer\build_installer.ps1
#   powershell -ExecutionPolicy Bypass -File NET\installer\build_installer.ps1 -Project src\Tiisu.Shell
#
# Steps:
#   1. dotnet publish app (framework-dependent, win-x64) -> NET\dist\publish
#   2. dotnet publish updater into the same folder (Tiisu.Updater.exe)
#   3. write version.json
#   4. makensis -> NET\dist\TiisuMahjong-Setup.exe
#
# NOTE: keep this file ASCII-only. PowerShell 5.1 reads .ps1 as ANSI (GBK) unless it has a BOM.

param(
    [string]$Project = 'src\Tiisu.App',
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$AppExe = 'Tiisu.Mahjong.exe'
)

$ErrorActionPreference = 'Stop'

$netRoot = Split-Path -Parent $PSScriptRoot        # ...\NET
$dist = Join-Path $netRoot 'dist'
$pub = Join-Path $dist 'publish'
$setup = Join-Path $dist 'TiisuMahjong-Setup.exe'

$nsisCandidates = @(
    'G:\Program Files (x86)\NSIS\makensis.exe',
    'C:\Program Files (x86)\NSIS\makensis.exe',
    'C:\Program Files\NSIS\makensis.exe'
)
$nsis = $null
foreach ($c in $nsisCandidates) {
    if (Test-Path $c) { $nsis = $c; break }
}
if (-not $nsis) {
    throw "makensis.exe not found. Install NSIS 3.x or edit the candidate list in this script."
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

Write-Host "== 1/4 clean dist =="
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $pub | Out-Null

Write-Host "== 2/4 dotnet publish app (framework-dependent) =="
Push-Location $netRoot
try {
    dotnet publish $Project -c $Configuration -r $Runtime --self-contained false -o $pub -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

    Write-Host "== 3/4 dotnet publish updater =="
    dotnet publish 'src\Tiisu.Updater' -c $Configuration -r $Runtime --self-contained false -o $pub -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw "updater publish failed ($LASTEXITCODE)" }
}
finally { Pop-Location }

$exe = Join-Path $pub $AppExe
if (-not (Test-Path $exe)) {
    throw "publish output does not contain $AppExe (check AssemblyName vs -AppExe)"
}

# version.json：替换式更新用它记录版本/更新时间
$version = @{
    version   = '0.1.0'
    buildTime = (Get-Date).ToString('s')
    app       = $AppExe
} | ConvertTo-Json -Compress
[System.IO.File]::WriteAllText((Join-Path $pub 'version.json'), $version, [System.Text.UTF8Encoding]::new($false))

# drop debug symbols: third-party pdb (libSkiaSharp 91MB) is useless at runtime
Get-ChildItem $pub -Recurse -File -Include *.pdb | Remove-Item -Force -ErrorAction SilentlyContinue
$sizeMb = [math]::Round((Get-ChildItem $pub -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
$files = @(Get-ChildItem $pub -Recurse -File).Count
Write-Host "   publish: $pub ($sizeMb MB, $files files)"

Write-Host "== 4/4 makensis =="
& $nsis "/DSRC=$pub" "/DOUTFILE=$setup" "/DAPPEXE=$AppExe" (Join-Path $PSScriptRoot 'installer.nsi')
if ($LASTEXITCODE -ne 0) { throw "makensis failed ($LASTEXITCODE)" }

if (-not (Test-Path $setup)) { throw "installer not produced: $setup" }
$setupMb = [math]::Round((Get-Item $setup).Length / 1MB, 2)
Write-Host ""
Write-Host "OK  installer: $setup ($setupMb MB)"
Write-Host "    installs to %LOCALAPPDATA%\Programs\TiisuMahjong, no registry writes"
Write-Host "    replace-update: Tiisu.Updater.exe --apply <new build dir or zip> --restart"
Write-Host "    requires .NET 10 Desktop Runtime (installer checks and prompts)"
