$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$dotnet = Join-Path $root 'tools\dotnet\dotnet.exe'
$project = Join-Path $root 'native\MicaBrowser.WinUI\MicaBrowser.WinUI.csproj'
$programsRoot = Join-Path $env:LOCALAPPDATA 'Programs'
$output = Join-Path $programsRoot 'EXET Browser'
$stage = Join-Path $root '.publish\EXET'
$targetExe = Join-Path $output 'EXET.exe'

if (-not (Test-Path $dotnet)) {
    throw "Portable .NET SDK not found at $dotnet."
}
if (-not (Test-Path $project)) {
    throw "Project not found at $project."
}
if (Get-Process EXET -ErrorAction SilentlyContinue) {
    throw 'EXET is running. Close it before rebuilding.'
}
if (Get-Process MicaBrowser -ErrorAction SilentlyContinue) {
    throw 'An old MicaBrowser process is running. Close it before rebuilding.'
}

New-Item -ItemType Directory -Path $programsRoot -Force | Out-Null
New-Item -ItemType Directory -Path $output -Force | Out-Null
if (Test-Path $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

Write-Host 'Publishing EXET (self-contained win-x64)...' -ForegroundColor Cyan
& $dotnet publish $project -c Release -r win-x64 --self-contained true -p:Platform=x64 -o $stage
if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $stage 'EXET.exe'))) {
    throw 'dotnet publish failed.'
}

Copy-Item (Join-Path $stage '*') $output -Recurse -Force
if (-not (Test-Path $targetExe)) {
    throw 'Could not install EXET into the user Programs directory.'
}

if (Test-Path (Join-Path $root 'README.md')) {
    Copy-Item (Join-Path $root 'README.md') (Join-Path $output 'README.md') -Force
}
if (Test-Path (Join-Path $root 'docs')) {
    Copy-Item (Join-Path $root 'docs') (Join-Path $output 'docs') -Recurse -Force
}

$startCommand = Join-Path $root 'Start EXET.vbs'
@'
Set shell = CreateObject("WScript.Shell")
path = shell.ExpandEnvironmentStrings("%LOCALAPPDATA%\Programs\EXET Browser\EXET.exe")
shell.Run """" & path & """", 1, False
'@ | Set-Content -LiteralPath $startCommand -Encoding ASCII

$vbsLauncher = Join-Path $output 'Launch EXET.vbs'
@'
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
shell.CurrentDirectory = fso.GetParentFolderName(WScript.ScriptFullName)
shell.Run "EXET.exe", 1, False
'@ | Set-Content -LiteralPath $vbsLauncher -Encoding ASCII

$shortcutPath = Join-Path $env:USERPROFILE 'Desktop\EXET.lnk'
$wsh = New-Object -ComObject WScript.Shell
$shortcut = $wsh.CreateShortcut($shortcutPath)
$shortcut.TargetPath = "$env:SystemRoot\System32\wscript.exe"
$shortcut.Arguments = "`"$vbsLauncher`""
$shortcut.WorkingDirectory = $output
$shortcut.IconLocation = "$targetExe,0"
$shortcut.Description = 'EXET - native Windows browser'
$shortcut.Save()

$size = (Get-ChildItem $output -Recurse -File | Measure-Object -Property Length -Sum).Sum
$version = (Get-Item $targetExe).VersionInfo
$displayVersionFile = Join-Path $root 'native\MicaBrowser.WinUI\version.txt'
$displayVersion = if (Test-Path $displayVersionFile) {
    (Get-Content $displayVersionFile -Raw).Trim()
} else {
    $version.ProductVersion
}
Write-Host ''
Write-Host 'Built EXET' -ForegroundColor Green
Write-Host "  Executable : $targetExe"
Write-Host "  Version    : $displayVersion (file $($version.ProductVersion))"
Write-Host "  Shortcut   : $shortcutPath"
Write-Host "  Size       : $([math]::Round($size / 1MB, 1)) MB"
