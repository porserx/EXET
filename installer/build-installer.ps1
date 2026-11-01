param(
    [string]$SourceDir = "$env:LOCALAPPDATA\Programs\EXET Browser",
    [string]$Iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = (Get-Content (Join-Path $root 'native\MicaBrowser.WinUI\version.txt') -Raw).Trim()
$iss = Join-Path $PSScriptRoot 'EXET.iss'
$tempIss = Join-Path $PSScriptRoot ".build-EXET.iss"

if (-not (Test-Path $SourceDir)) {
    throw "App directory not found: $SourceDir"
}
if (-not (Test-Path $Iscc)) {
    throw "Inno Setup compiler not found: $Iscc"
}

$content = Get-Content $iss -Raw -Encoding UTF8
$content = $content -replace '#define MyAppVersion "[^"]+"', "#define MyAppVersion `"$version`""
$content = $content -replace '#define SourceDir "[^"]+"', "#define SourceDir `"$SourceDir`""
Set-Content -LiteralPath $tempIss -Value $content -Encoding UTF8

& $Iscc $tempIss
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE"
}

$setup = Join-Path $root "dist\EXET-Setup-$version.exe"
if (-not (Test-Path $setup)) {
    throw "Installer was not produced: $setup"
}

Get-Item $setup | Select-Object FullName, Length, LastWriteTime
Get-FileHash $setup -Algorithm SHA256 | Select-Object Hash
