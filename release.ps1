# Builds the update package that Desktop Drawer downloads from GitHub Releases.
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Notes = '',

    [switch]$Publish
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Version must look like 1.2.3.'
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\DesktopDrawer\DesktopDrawer.csproj'
$watchdog = Join-Path $root 'src\DesktopDrawer.Watchdog\DesktopDrawer.Watchdog.csproj'
$stage = Join-Path $env:TEMP "DesktopDrawer-release\$Version"
$zip = Join-Path $env:TEMP 'DesktopDrawer-win-x64.zip'
$checksum = "$zip.sha256"

if (Test-Path $stage) {
    Remove-Item $stage -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $stage | Out-Null

Write-Host "Publishing Desktop Drawer $Version ..."
dotnet publish $project -c Release -r win-x64 --self-contained false -p:Version=$Version -o $stage
if ($LASTEXITCODE -ne 0) {
    throw "DesktopDrawer publish failed with exit code $LASTEXITCODE."
}

dotnet publish $watchdog -c Release -r win-x64 --self-contained false -o $stage
if ($LASTEXITCODE -ne 0) {
    throw "Watchdog publish failed with exit code $LASTEXITCODE."
}

if (Test-Path $zip) {
    Remove-Item $zip -Force
}

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path $checksum -Value "$hash  DesktopDrawer-win-x64.zip" -NoNewline

Write-Host "Package:  $zip"
Write-Host "Checksum: $checksum"

if (-not $Publish) {
    Write-Host 'Upload it as a GitHub release asset named DesktopDrawer-win-x64.zip.'
    Write-Host 'Or rerun with -Publish to create the release with the GitHub CLI.'
    exit 0
}

gh --version | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'The GitHub CLI (gh) is required for -Publish. Run gh auth login first.'
}

$tag = "v$Version"
if ([string]::IsNullOrWhiteSpace($Notes)) {
    $Notes = "Desktop Drawer $Version"
}

gh release view $tag --repo BEGINWITHF/Desktop-Drawer 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
    gh release upload $tag $zip $checksum --repo BEGINWITHF/Desktop-Drawer --clobber
} else {
    gh release create $tag $zip $checksum --repo BEGINWITHF/Desktop-Drawer --title "Desktop Drawer $Version" --notes $Notes
}

if ($LASTEXITCODE -ne 0) {
    throw 'Publishing the GitHub release failed.'
}

Write-Host "Published $tag."
