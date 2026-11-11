# Terrarium development guide

Terrarium is a GPL-3.0 fork of Deskland Preview (upstream remote: `upstream`, https://github.com/BLEZZ0102/Deskland). Keep the attribution in README.md, NOTICE and TRADEMARKS.md.

## Ground rules
- Product name is `Terrarium`. Never reintroduce the Deskland name or icon in anything user-visible (window titles, tray, installer, settings, theme panels). Internal identifiers inherited from upstream (`DESKLAND_THUMBS`, `desklandSnapshot`, `desklandRecord`) are kept on purpose to ease upstream merges.
- Data lives in `%LOCALAPPDATA%\Terrarium`; installs go to `Programs\Terrarium`; Run key, startup shortcut, mutex and event names all use `Terrarium` so it can coexist with Deskland.
- All themes are free and complete. There is no locked/purchase/entitlement code, sign-in or update service; don't add any.
- English only: theme names, descriptions and parameter text live in `themes.json`; settings-window and tray strings in `i18n/en.json`. Comments, logs and sample data are English; keep it that way.
- Don't commit `host/bin/`, `host/lib/`, `dist/`, personal data, credentials or machine paths.

## Build and preview
- Host: `powershell -ExecutionPolicy Bypass -File host\build.ps1` → `host\bin\Terrarium.exe`. Theme HTML/JS changes don't need a rebuild; use tray → Reload. C# changes need exit + rebuild.
- Browser preview: `node prototype\serve.mjs`, then http://localhost:5178/settings.html or a theme page directly.
- Installer: `powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1` → `dist\`. It packages every theme listed in `themes.json`, the scripts each page loads, and everything in `prototype\assets`.
- App icon: edit `host/icon/terrarium.svg`, then regenerate `host/Terrarium.ico` (16–64 px as 32-bit DIB, 256 px as PNG).

## Theme page contract
- One page per theme in `prototype/`, registered in `themes.json` (`status: "ready"`, `product: "free"`, `adaptive: true`, `groups` of params with defaults) and with a thumbnail in `prototype/thumbs/<id>.webp` plus an inline copy in `thumbs-data.js`.
- Modes: `?wallpaper=1` (host via `chrome.webview`; post `'ready'`), `?preview=1` (settings iframe; post `{t:'ready'}` to parent and draw ghost icons), no query = standalone demo desktop with draggable sample icons and a dev panel.
- Messages in: `icons` (`r` = physical-pixel rects, divide by devicePixelRatio), `settings` (partial values, may include `seed`), `reset`, `pause`.
- `share.js` loads last and needs page globals `cv` (with `cv._scale`), `draw(now)`, `icons`, `W`, `H`, `WALL`.
- Icon labels are white text drawn by Windows: keep the scenery directly under icons mid-tone or darker.

## Performance
- Wallpaper runs all day: target 30 fps on integrated graphics (`cap30`), pause when the host says so.
- Cache static drawing in textures rebuilt only when the icon field changes; draw small moving objects in local/scissored passes; never loop over many objects per pixel in the full-screen shader.
- Size canvases from the real CSS box times the device scale (fractional sizes) so the wallpaper stays sharp.
- After changing a theme, check icon moves, the settings preview, pause and sharing.
