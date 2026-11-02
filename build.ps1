# Publishes Desktop Drawer to %LocalAppData%\DesktopDrawer
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\DesktopDrawer\DesktopDrawer.csproj'
$target = Join-Path $env:LOCALAPPDATA 'DesktopDrawer'

# The app must not be running, otherwise the published files stay locked.
$running = Get-Process DesktopDrawer -ErrorAction SilentlyContinue
if ($running) {
    Write-Host 'Desktop Drawer is running. Exit it from the settings panel (or the tray menu) first.'
    exit 1
}

Write-Host "Publishing to $target ..."
dotnet publish $project -c $Configuration -r win-x64 --self-contained false -o $target | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# Point the start-with-Windows entry at the published executable.
$exe = Join-Path $target 'DesktopDrawer.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$current = (Get-ItemProperty -Path $runKey -Name DesktopDrawer -ErrorAction SilentlyContinue).DesktopDrawer
if ($current) {
    Set-ItemProperty -Path $runKey -Name DesktopDrawer -Value "`"$exe`""
    Write-Host 'Updated the startup entry.'
}

Write-Host "Done: $exe"
