# Terrarium

**An adaptive Windows wallpaper that makes your desktop icons part of the scenery.**

Terrarium turns the space around your desktop icons into a small living world. Move, add or remove an icon and the landscape quietly reshapes around it in real time. Everything runs locally: no AI, no account, no network service.

Terrarium is a fork of [Deskland Preview](https://github.com/BLEZZ0102/Deskland) by the Deskland authors, released under GPL-3.0. It adds four new themes, drops the locked paid-edition previews and ships under its own name and icon. It is not affiliated with or endorsed by the Deskland project.

![Meadow](docs/images/meadow.webp)

## Themes

| Theme | What you see |
| --- | --- |
| **Meadow** | Pen-and-wash grassland; icons rest in drifting white clouds with a boy flying a kite and a little black cat. *(from Deskland)* |
| **Beach** | Turquoise waves, kayaks, seabirds and coconut palms around your icons. *(from Deskland)* |
| **Night Sky** | Glowing nebula clouds around your icons, layered twinkling stars, shooting stars and a slowly orbiting planet. |
| **Snowy Mountains** | Snowy clearings in a pine forest, drifting snowfall and a cabin with a smoking chimney. |
| **Coral Reef** | Sandy reef flats ringed by coral, caustic light, schools of fish and a passing sea turtle. |
| **City** | Small parks around your icons inside a miniature city with traffic and a day/night cycle that follows your clock. |

Every theme has its own settings, saved between sessions.

## Features

- **Adaptive scenery:** reads the real positions of desktop icons and transitions smoothly when they move.
- **Made for the desktop:** tray controls, start on sign-in, automatic pause when a window is maximized or full-screen; targets 30 fps on integrated graphics.
- **Private sharing:** image and animation exports draw sample icons and color blocks, never a screenshot of your desktop or real file names.
- **English and Simplified Chinese** interface.

## Requirements

- Windows 10 or Windows 11, 64-bit.
- Microsoft Edge WebView2 Runtime (preinstalled on Windows 11).
- PowerShell and the .NET Framework C# compiler included with Windows; no .NET SDK needed.
- An internet connection for the first build, which downloads the WebView2 SDK from NuGet.
- Node.js only for the browser preview.

Terrarium currently renders on the **primary monitor** only.

## Build from source

Open PowerShell in the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File host\build.ps1
host\bin\Terrarium.exe
```

Keep `host/` and `prototype/` together. Build output goes to `host/bin/`.

Run the program again or double-click its tray icon to open Settings. To exit and restore your original wallpaper, use the tray menu or run:

```powershell
host\bin\Terrarium.exe --exit
```

Settings and logs are stored in `%LOCALAPPDATA%\Terrarium`.

## Browser preview

```powershell
node prototype\serve.mjs
```

Open **http://localhost:5178/settings.html** for the settings window, or a theme page directly (for example `http://localhost:5178/space.html`) to try it with draggable sample icons. Adapting to your real desktop icons requires the Windows host.

## Windows installer

```powershell
powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1
```

The first build downloads portable NSIS and Microsoft's WebView2 bootstrapper. Artifacts are written to `dist/` with SHA256 checksums and a matching source ZIP (required by the GPL). The installer targets the current user's `Programs\Terrarium` folder and needs no administrator rights. It is unsigned, so Windows SmartScreen may show a warning.

## Project layout

| Path | Contents |
| --- | --- |
| `host/` | Windows host (C# / WebView2), build script, manifest and icon (`host/icon/terrarium.svg` is the source) |
| `prototype/themes.json` | Theme registry: page, parameters and defaults for every theme |
| `prototype/meadow.html`, `flat.html` | Meadow and Beach themes |
| `prototype/space.html`, `snow.html`, `reef.html`, `city.html` | Night Sky, Snowy Mountains, Coral Reef and City themes |
| `prototype/settings.html` | Settings window |
| `prototype/share.js` | Privacy-preserving image and animation sharing |
| `prototype/i18n/` | Interface strings (English, Simplified Chinese) |
| `prototype/assets/` | Images used by the themes |
| `tools/` | Installer build tooling |

## Adding a theme

A theme is one HTML page in `prototype/` plus an entry in `themes.json`. Start from `meadow.html`: it receives icon rectangles from the host, computes a distance field around them and draws the scene in a WebGL2 shader. See `AGENTS.md` for the page protocol and performance rules.

## Known limitations

- Only the primary monitor is supported.
- Windows Spotlight or a wallpaper slideshow can briefly flash when Windows changes the background image.

## Credits and license

Based on [Deskland Preview](https://github.com/BLEZZ0102/Deskland) by the Deskland authors. Source code and included theme assets are released under [GPL-3.0](LICENSE); modified versions must stay under GPL-3.0 and ship their source. Third-party dependencies are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md); naming is covered in [TRADEMARKS.md](TRADEMARKS.md).
