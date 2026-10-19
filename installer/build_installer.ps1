# Build the NSIS installer for Tiisu Mahjong (PowerShell 5.1)
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File NET\installer\build_installer.ps1
#   powershell -ExecutionPolicy Bypass -File NET\installer\build_installer.ps1 -Project src\Tiisu.Shell
#
# Steps:
#   1. dotnet publish (framework-dependent, win-x64) -> NET\dist\publish
#   2. makensis -> NET\dist\TiisuMahjong-Setup.exe
#   3. sanity checks
#
# NOTE: keep this file ASCII-only. PowerShell 5.1 reads .ps1 as ANSI (GBK) unless it has a BOM.

param(
    [string]$Project = 'src\Tiisu.Shell',
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

Write-Host "== 1/3 clean dist =="
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $pub | Out-Null

Write-Host "== 2/3 dotnet publish (framework-dependent) =="
Push-Location $netRoot
try {
    dotnet publish $Project -c $Configuration -r $Runtime --self-contained false -o $pub -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
}
finally { Pop-Location }

$exe = Join-Path $pub $AppExe
if (-not (Test-Path $exe)) {
    throw "publish output does not contain $AppExe (check AssemblyName vs -AppExe)"
}
$cssCount = 0
if (Test-Path (Join-Path $pub 'Web')) {
    $cssCount = @(Get-ChildItem (Join-Path $pub 'Web') -Recurse -File).Count
}
$sizeMb = [math]::Round((Get-ChildItem $pub -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "   publish: $pub ($sizeMb MB, Web files: $cssCount)"

Write-Host "== 3/3 makensis =="
& $nsis "/DSRC=$pub" "/DOUTFILE=$setup" "/DAPPEXE=$AppExe" (Join-Path $PSScriptRoot 'installer.nsi')
if ($LASTEXITCODE -ne 0) { throw "makensis failed ($LASTEXITCODE)" }

if (-not (Test-Path $setup)) { throw "installer not produced: $setup" }
$setupMb = [math]::Round((Get-Item $setup).Length / 1MB, 2)
Write-Host ""
Write-Host "OK  installer: $setup ($setupMb MB)"
Write-Host "    installs to %LOCALAPPDATA%\Programs\TiisuMahjong, no registry writes"
Write-Host "    requires .NET 10 Desktop Runtime (installer checks and prompts)"
