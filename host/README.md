# Terrarium Windows host

Uses C# / .NET Framework and WebView2 to place theme pages below the Windows desktop icon layer.

## Build and run

```powershell
powershell -ExecutionPolicy Bypass -File host\build.ps1
host\bin\Terrarium.exe
```

The build script uses the C# compiler that ships with Windows and, on first run, downloads a pinned version of the WebView2 SDK from NuGet. Dependencies are kept in `host/lib/` and the output goes to `host/bin/`; neither is committed to the repository.

## Command-line options

| Option | Behavior |
| --- | --- |
| (none) | Starts the wallpaper and opens settings; if already running, opens settings |
| `--autostart` | Starts in the background without opening settings |
| `--settings` | Opens settings |
| `--menu` | Opens the tray menu |
| `--exit` | Exits cleanly and restores the system wallpaper |
| `--exit-installed` | Used by the installer: exits only an instance running from the current executable path |

## Behavior and data

- Themes come from `prototype/themes.json`; only themes with status `ready` are loaded.
- Desktop icon positions are checked every 400ms, and the theme page is notified only when they change.
- Playback pauses while the foreground window is maximized or fullscreen; starting the wallpaper doesn't steal focus, and the host's own windows, invisible windows and cloaked windows never trigger a pause by mistake. The tray menu can pause and resume manually. The settings page refreshes its cache per program version, and thumbnails use embedded images in real `<img>` elements.
- Windows 11 24H2 and later use a layered child window of Progman; earlier versions use WorkerW.
- Only the primary monitor is covered for now; a system wallpaper slideshow or Windows Spotlight image change may cause a brief flicker.
- Settings are saved to `%LOCALAPPDATA%\Terrarium\settings.json`; the log is `host.log` in the same folder.
- The interface is English only.
- No account sign-in, purchase restore or automatic update checks; nothing goes online to check for a new version at startup or when settings open.
- Sharing uses only the icon positions: the theme page draws placeholder icons and colored blocks for the names, and never captures the desktop windows.
- After changing a theme page, use "Reload" in the tray menu; after changing the host C# code, exit and rebuild.

For installer testing, set the environment variable `TERRARIUM_PROFILE_DIR` to an absolute path to isolate settings, the WebView2 cache and the single-instance events; in this mode start on sign-in is never written. Normal launches don't set this variable and use the regular data folder.

## Page messaging

The settings page sends `hello`, `set`, `reset`, `theme`, `autostart` and the share-related messages; the host sends the initial settings, icon rectangles and share results.

After a theme page sends `ready`, the host passes the settings and then the icon positions; after that it passes parameter changes, icon changes and the pause state. Settings and theme pages are both loaded from a local virtual host name.
