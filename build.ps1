# Publishes Desktop Drawer to %LocalAppData%\DesktopDrawer
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\DesktopDrawer\DesktopDrawer.csproj'
$watchdog = Join-Path $root 'src\DesktopDrawer.Watchdog\DesktopDrawer.Watchdog.csproj'
$target = Join-Path $env:LOCALAPPDATA 'DesktopDrawer'

# The app must not be running, otherwise the published files stay locked.
$running = Get-Process DesktopDrawer, DesktopDrawer.Watchdog -ErrorAction SilentlyContinue
if ($running) {
    Write-Host 'Desktop Drawer is running. Exit it from the settings panel (or the tray menu) first.'
    exit 1
}

Write-Host "Publishing to $target ..."
dotnet publish $project -c $Configuration -r win-x64 --self-contained false -o $target | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# The watchdog is a separate executable so a forced kill of the app cannot take it down.
dotnet publish $watchdog -c $Configuration -r win-x64 --self-contained false -o $target | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish (watchdog) failed with exit code $LASTEXITCODE"
}

# Point the start-with-Windows entry at the published executable.
$exe = Join-Path $target 'DesktopDrawer.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$current = (Get-ItemProperty -Path $runKey -Name DesktopDrawer -ErrorAction SilentlyContinue).DesktopDrawer
if ($current) {
    Set-ItemProperty -Path $runKey -Name DesktopDrawer -Value "`"$exe`""
    Write-Host 'Updated the startup entry.'
}

# A Start menu shortcut is what makes the app show up in the Windows key app
# list and in Start search.
$programs = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
New-Item -ItemType Directory -Force -Path $programs | Out-Null

$shortcutPath = Join-Path $programs 'Desktop Drawer.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $target
$shortcut.IconLocation = "$exe,0"
$shortcut.Description = 'A paged desktop'
$shortcut.WindowStyle = 7
$shortcut.Save()
Write-Host "Start menu shortcut: $shortcutPath"

Write-Host "Done: $exe"
