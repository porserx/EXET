param(
    [switch]$Bump
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$versionFile = Join-Path $root 'native\MicaBrowser.WinUI\version.txt'
if (-not (Test-Path $versionFile)) {
    throw "Version file not found: $versionFile"
}

$current = (Get-Content $versionFile -Raw).Trim()
if ($current -notmatch '^(\d+)\.(\d+)\.(\d+)$') {
    throw "Invalid semantic version in ${versionFile}: $current"
}

$major = [int]$Matches[1]
$minor = [int]$Matches[2]
$patch = [int]$Matches[3]
if ($Bump) {
    $patch++
}
$next = "$major.$minor.$patch"
Set-Content -LiteralPath $versionFile -Value $next -Encoding UTF8

# The files below are generated views of version.txt, not independent sources.
$csproj = Join-Path $root 'native\MicaBrowser.WinUI\MicaBrowser.WinUI.csproj'
if (Test-Path $csproj) {
    $content = Get-Content $csproj -Raw -Encoding UTF8
    $content = $content -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$next.0</AssemblyVersion>"
    $content = $content -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$next.0</FileVersion>"
    $content = $content -replace '<Version>[^<]+</Version>', "<Version>$next</Version>"
    Set-Content -LiteralPath $csproj -Value $content -Encoding UTF8 -NoNewline
}

foreach ($iss in @(
    (Join-Path $root '.publish\installer\EXET.iss'),
    (Join-Path $root 'installer\EXET.iss')
)) {
    if (-not (Test-Path $iss)) { continue }
    $content = Get-Content $iss -Raw -Encoding UTF8
    $content = $content -replace '#define MyAppVersion "[^"]+"', "#define MyAppVersion `"$next`""
    Set-Content -LiteralPath $iss -Value $content -Encoding UTF8 -NoNewline
}

$readme = Join-Path $root 'README.md'
if (Test-Path $readme) {
    $content = Get-Content $readme -Raw -Encoding UTF8
    $content = $content -replace '(?m)^版本：.*$', "版本：$next"
    Set-Content -LiteralPath $readme -Value $content -Encoding UTF8 -NoNewline
}

Write-Output $next
