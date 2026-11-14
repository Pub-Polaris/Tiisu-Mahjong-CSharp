<#
.SYNOPSIS
  从 dist/publish 生成 Steam depot 内容目录与校验清单。

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\steam_package.ps1
#>
param(
    [string]$PublishDir = "dist/publish",
    [string]$OutDir = "dist/steam",
    [switch]$Zip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Write-Utf8([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Resolve-RepoPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $root $Path))
}

[string]$publish = (Resolve-RepoPath $PublishDir).Trim()
[string]$out = (Resolve-RepoPath $OutDir).Trim()
$exe = Join-Path $publish "Tiisu.Mahjong.exe"
if (-not (Test-Path $exe)) {
    throw "找不到 $exe，先运行 installer/build_installer.ps1 或 dotnet publish"
}

$devTools = Join-Path $root "tools\Tiisu.DevTools\bin\Debug\net10.0-windows10.0.19041\Tiisu.DevTools.exe"
if (-not (Test-Path $devTools)) {
    throw "找不到 DevTools：$devTools，先 dotnet build tools/Tiisu.DevTools"
}

[string]$version = ("$((& $exe --version 2>&1 | Select-Object -First 1))").Trim()
& $devTools --check-publish $publish | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "发布自检失败（exit $LASTEXITCODE）"
}

[string]$depot = (Join-Path $out "depot").Trim()
if (Test-Path $depot) {
    Remove-Item $depot -Recurse -Force
}
New-Item -ItemType Directory -Path $depot -Force | Out-Null
Copy-Item (Join-Path $publish "*") $depot -Recurse -Force
Remove-Item (Join-Path $depot "*.pdb") -Force -ErrorAction SilentlyContinue

$files = Get-ChildItem $depot -Recurse -File | Sort-Object FullName
$entries = foreach ($file in $files) {
    $relative = $file.FullName.Substring($depot.Length + 1).Replace("\", "/")
    [ordered]@{
        path = $relative
        size = $file.Length
        sha256 = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$manifest = [ordered]@{
    version = "$version".Trim()
    generatedAt = (Get-Date).ToString("o")
    publishDir = $publish
    depotDir = $depot
    fileCount = @($entries).Count
    totalBytes = ($entries | ForEach-Object { [int64]$_.size } | Measure-Object -Sum).Sum
    files = @($entries)
}
Write-Utf8 (Join-Path $out "depot-manifest.json") (($manifest | ConvertTo-Json -Depth 5) + "`r`n")

$buildLines = @(
    "version: $version",
    "generated: $(Get-Date -Format o)",
    "publish: $publish",
    "depot: $depot",
    "files: $(@($entries).Count)"
)
Write-Utf8 (Join-Path $out "build.txt") (($buildLines -join "`r`n") + "`r`n")

$descLine = '    "Desc" "Tiisu Mahjong ' + $version + '"'
$contentLine = '    "ContentRoot" "' + $depot.Replace('\', '/') + '"'
$vdfLines = @(
    '"AppBuild"',
    '{',
    '    "AppID" "REPLACE_WITH_APPID"',
    $descLine,
    '    "BuildOutput" "output/"',
    $contentLine,
    '    "Depots"',
    '    {',
    '        "DEPOT_ID" "depot_build.vdf"',
    '    }',
    '}'
)
Write-Utf8 (Join-Path $out "app_build.vdf.template") (($vdfLines -join "`r`n") + "`r`n")

$depotVdfLines = @(
    '"DepotBuildConfig"',
    '{',
    '    "DepotID" "REPLACE_WITH_DEPOT_ID"',
    '    "ContentRoot" "depot"',
    '    "FileMapping"',
    '    {',
    '        "LocalPath" "*"',
    '        "DepotPath" "."',
    '        "recursive" "1"',
    '    }',
    '    "FileExclusion" "*.pdb"',
    '}'
)
Write-Utf8 (Join-Path $out "depot_build.vdf.template") (($depotVdfLines -join "`r`n") + "`r`n")

if ($Zip) {
    $zip = Join-Path $out ("Tiisu-Depot-" + ($version -replace '[^\w\.-]', '_') + ".zip")
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $depot "*") -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "zip: $zip"
}

Write-Host "steam depot: $depot"
Write-Host "version: $version"
Write-Host "files: $(@($entries).Count)"
Write-Host "manifest: $(Join-Path $out 'depot-manifest.json')"
