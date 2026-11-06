# Builds the per-user Desktop Drawer installer with Inno Setup.
param(
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Version must look like 1.2.3.'
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$iss = Join-Path $root 'installer\DesktopDrawer.iss'
$outputDir = Join-Path $root 'installer\Output'
$stage = Join-Path $env:TEMP "DesktopDrawer-release\$Version"
$productVersion = "$Version.0"

$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
)

$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    $command = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($command) {
        $iscc = $command.Source
    }
}

if (-not $iscc) {
    throw 'Inno Setup 6 is required. Install it with: winget install JRSoftware.InnoSetup'
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

# Reuse the same publish stage as the update package.
& (Join-Path $root 'release.ps1') -Version $Version
if (-not (Test-Path (Join-Path $stage 'DesktopDrawer.exe'))) {
    throw "The publish folder is incomplete: $stage"
}

Write-Host "Compiling installer with $iscc ..."
& $iscc `
    "/DAppVersion=$Version" `
    "/DProductVersion=$productVersion" `
    "/DSourceDir=$stage" `
    "/DOutputDir=$outputDir" `
    $iss

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installer = Join-Path $outputDir "DesktopDrawer-Setup-$Version.exe"
if (-not (Test-Path $installer)) {
    throw "The installer was not created: $installer"
}

$hash = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$installer.sha256" -Value "$hash  DesktopDrawer-Setup-$Version.exe" -NoNewline

$size = [math]::Round((Get-Item $installer).Length / 1MB, 2)
Write-Host "Installer: $installer"
Write-Host "Size:      $size MB"
Write-Host "Checksum:  $installer.sha256"
