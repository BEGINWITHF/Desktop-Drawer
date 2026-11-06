<p align="center">
  <img src="docs/banner.svg" alt="Desktop Drawer - a paged desktop for Windows" width="100%">
</p>

# Desktop Drawer

Turn the Windows desktop into a **paged desktop**. Desktop Drawer never moves, renames, or deletes your files. It draws a native-like icon grid over the wallpaper so a crowded desktop stays manageable.

- Icons are drawn directly on the wallpaper: no large title bar and no window frame.
- The window is **truly transparent**. The wallpaper shows through completely, including wallpaper changes, slideshows, and live wallpapers.
- It never appears in the taskbar, Alt+Tab, or the window switcher. After `Win+D`, it pins itself back to the desktop.
- It always sits above the desktop and below application windows, so it never covers a running app.
- Native Windows desktop icons are hidden on launch by default and restored on exit.
- Starts with Windows by default, taking over the desktop after sign-in.
- Page with the mouse wheel, `PageUp` / `PageDown`, or the page dots.
- The header and footer fade out after about 2.4 seconds of inactivity and return as soon as the mouse moves.

## Installation

Requires Windows 10/11 and the .NET 9 Desktop Runtime (the SDK also works).

```powershell
.\build.ps1
```

The script publishes to `%LocalAppData%\DesktopDrawer` and points the current user's startup entry at that folder. Run:

```text
%LocalAppData%\DesktopDrawer\DesktopDrawer.exe
```

It also creates (and refreshes) a **Desktop Drawer** shortcut in the Start menu, so opening the app list with `Win` or searching for the name finds it.

The app is single-instance; launching it again will not create a second desktop.

### Installer

```powershell
.\installer.ps1 -Version 1.0.0
```

Creates `installer\Output\DesktopDrawer-Setup-1.0.0.exe`:

- Installs per-user to `%LocalAppData%\DesktopDrawer` with no administrator rights.
- Adds a Start menu shortcut and registers an uninstall entry in Apps & Features.
- Checks for the .NET 9 Desktop Runtime before installing.
- Installer UI in English / 简体中文 / 繁體中文 / 日本語.
- Settings, layout, and colors in `%APPDATA%\DesktopDrawer` survive updates and uninstalls.
- Requires Inno Setup 6 locally: `winget install JRSoftware.InnoSetup`.

Pushing a `v*` tag makes GitHub Actions build the installer and attach it to the release. The installer is currently unsigned, so SmartScreen may warn on first launch.

## Shortcuts

| Action | How |
| --- | --- |
| Flip pages | Mouse wheel, two-finger pan on a touchpad, `PageUp` / `PageDown`, `Alt+←` / `Alt+→`, or the page dots |
| First / last page | `Ctrl+Home` / `Ctrl+End` |
| Search | `Ctrl+F` |
| Open | Double-click, or select and press `Enter` |
| Select | Click, `Ctrl+click`, `Shift+click`, or the arrow keys |
| View full file name | Click a truncated icon label; the label expands to show the full name |
| Rename | `F2` |
| Properties | `Alt+Enter` |
| Delete to Recycle Bin | `Delete` |
| Delete permanently | `Shift+Delete` |
| Copy files | `Ctrl+C` |
| Cut files | `Ctrl+X` |
| Paste to desktop | `Ctrl+V` |
| Copy file path | `Ctrl+Shift+C` |
| Select all on current page | `Ctrl+A` |
| Type-ahead | Start typing a file name to select the first matching item |
| Change icon size | `Ctrl+wheel`, or `Ctrl+Shift+1/2/3` (Compact / Cozy / Large) |
| Refresh | `F5` |
| New folder | `Ctrl+Shift+N` |
| Toggle native desktop icons | `Ctrl+Shift+H` |
| Close search / close panel | `Esc` |
| Other shortcuts | Any other `Ctrl` / `Alt` / `Win` combination is forwarded to the native desktop: the matching icon is selected, the desktop is brought forward, and the key is injected into the system, so third-party tools that hook the desktop respond exactly as they would on the native desktop |
| Context menu | Right-click an icon for the app-style menu; the last item, **More options...** (or `Shift`+right-click), opens the native Windows menu with all installed shell extensions |
| Empty-area right-click | New folder, refresh, grouping, hide native icons, settings, **restart**, or exit |
| Drag out | Drag an icon to File Explorer, a chat window, or any other drop target |
| Drag in | Drop files from File Explorer onto the desktop area or a folder icon; same-volume moves and cross-volume copies are the default, with `Ctrl` to copy and `Shift` to move |

## Settings panel

The gear at the top right opens settings. If the header is hidden, move the mouse first; you can also reopen the window from the tray icon.

- **Language**: English / 简体中文 / 繁體中文 / 日本語. Changes apply immediately to the window title, menus, tray, and toasts. First launch follows the system language.
- **Icon size**: Compact / Cozy / Large presets control how many icons fit on each page.
- **Custom icon size**: A 24–160 px slider; dragging overrides the preset.
- **Text size**: 80%–140% global scale for headings, buttons, menus, the search box, and desktop icon names.
- **Auto**: Auto buttons next to icon size, text size, wallpaper dim, columns, and rows return to automatic values (density preset / 100% / no dim / fit to window).
- **Colors**: Accent, three text levels, hover surfaces, panels, borders, danger, and shade are editable. Click any row to open the RGB picker (standard swatches + R/G/B sliders and values + Hex). The overlay previews changes live; Apply saves, Reset restores defaults.
- **Wallpaper dim**: 0–60% dims the wallpaper behind the overlay only.
- **Fit to window / Columns / Rows**: When fit is off, choose 2–16 columns and 1–16 rows. Tiles shrink automatically if they do not fit.
- **Folders first**: Keep folders above files when sorting.
- **Show hidden files**: Include hidden and system items from the desktop.
- **Always show controls**: Keep the header and page bar visible instead of fading them out.
- **Close to tray**: Keep Desktop Drawer running when the window is closed.
- **Start with Windows**: Start Desktop Drawer automatically after sign-in.
- **Hide desktop icons now**: Hide or restore the native Windows desktop icons immediately.
- **Hide icons on launch**: Hide native icons automatically when Desktop Drawer starts.
- **Open Desktop folder / Refresh icons**: Open the desktop folder and re-read icons.

All settings save instantly.

## One-click updates

The **Update now** button in ABOUT checks the latest GitHub Release, downloads the update package, verifies its SHA-256, exits the overlay, replaces the files, and restarts automatically.

- Source: `DesktopDrawer-win-x64.zip` from GitHub Releases.
- The version comes from the `vX.Y.Z` release tag; the current version is shown above the button.
- Download and replacement log: `%APPDATA%\DesktopDrawer\update.log`.
- Settings, layout, and colors live in `%APPDATA%\DesktopDrawer` and are not touched by updates.
- If no newer release exists, it reports that you are up to date.

Publishing a new version (requires GitHub CLI login):

```powershell
.\release.ps1 -Version 1.0.1 -Publish
```

Or push a `v1.0.1` tag and GitHub Actions builds and creates the release automatically. The update asset must remain named `DesktopDrawer-win-x64.zip`.

## Native desktop icons

At launch, Desktop Drawer hides the native Windows desktop icons (the same switch as Desktop right-click → View → Show desktop icons). Whether the app exits normally, crashes, or is killed from Task Manager, the icons are restored as soon as the paged desktop disappears. Hiding to the tray also restores them first and hides them again when the window is reopened.

`DesktopDrawer.Watchdog.exe` handles this. It is started by the main app, so Task Manager shows two Desktop Drawer-related processes. The watchdog has no UI and a tiny footprint. When the main process disappears, it restores the icons and exits, so you never need to end it manually. If the watchdog is killed on its own, the main app starts another one within 30 seconds.

Restoration checks whether the desktop icon list window is actually visible, not only the `HideIcons` registry value. Even after a forced kill, when the registry and the screen disagree, the icons are really shown again.

Only killing both processes (End process tree, `taskkill /T`) leaves nobody to restore them. Manual fix:

- Desktop right-click → View → Show desktop icons.
- Or start Desktop Drawer once and exit normally from the settings panel (it also restores icons on exit).

## Layout and grouping

**Adjust layout**

Settings → LAYOUT → **Adjust layout** (the settings panel closes automatically). The header, pager, group badge, bottom-left summary, and bottom-right hint get dashed outlines and three resize grips, and a floating toolbar slides in from the right:

- Drag an element to move it.
- The grips control width, height, and both: the right grip changes width, the bottom grip changes height, and the corner grip changes both. Content reflows at the new size instead of stretching.
- **Reset** on the toolbar restores default positions and sizes; **Done** exits editing; `Esc` also exits.
- Positions and sizes are stored proportionally in `Layout` in `settings.json`, so they survive resolution changes.
- The header spans the full row, so it can only move vertically; the other elements move and resize freely.

**Virtual grouping**

Desktop right-click → **Group by** (also in the sort menu) groups by **Type** (folders, documents, images, videos, audio, archives, programs, other), the first letter of **Name**, or **Date modified**.

- Files are never moved, renamed, or deleted; icons in the same group are just placed on the same page range.
- The pager shows the current group, for example `Images · 2/3`; the top-left badge shows the group name and count.
- The group badge can also be moved in Adjust layout.
- Choose **None** to return to ungrouped pages.

## Data locations

| Content | Path |
| --- | --- |
| Settings | `%APPDATA%\DesktopDrawer\settings.json` |
| Log | `%APPDATA%\DesktopDrawer\desktop-drawer.log` |
| App | `%LocalAppData%\DesktopDrawer\` |
| Startup entry | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\DesktopDrawer` |

Delete `settings.json` to reset settings, including re-hiding native icons and starting with Windows.

## Development

```powershell
dotnet build src\DesktopDrawer\DesktopDrawer.csproj
dotnet run --project src\DesktopDrawer\DesktopDrawer.csproj
```

The stack is .NET 9 + WPF with no third-party NuGet dependencies:

```text
src\DesktopDrawer
├─ MainWindow.xaml            UI (wallpaper layer, icon grid, header, floating pager, settings)
├─ MainWindow.xaml.cs         layout, paging, icon sizing, wallpaper, header auto-hide
├─ MainWindow.Interactions.cs keyboard/mouse, search, rename, delete, drag and drop
├─ MainWindow.Settings.cs     tray, settings panel, native icon toggle, exit
├─ MainWindow.Menus.cs        sort menu, item context menu, empty-area menu
├─ Services\                  desktop enumeration, icon extraction, wallpaper, settings, startup, shell operations
├─ Interop\NativeMethods.cs   Win32 / Shell declarations
└─ Themes\Theme.xaml          colors, buttons, toggles, and other styles

src\DesktopDrawer.Watchdog      separate watchdog that restores native icons when the app exits
```

## Known limitations

- It is a top-level tool window (hidden from the taskbar and Alt+Tab). `Win+D` minimizes it with the other windows; the app immediately restores and re-pins itself above the desktop, so there may be a very brief flash.
- It only reads the user desktop and public desktop folders; it never moves or reorganizes the original files.
- Icon sizes scale automatically at 100% DPI and above; extreme custom scaling may need a small density adjustment.

## License

MIT. See [LICENSE](LICENSE).
