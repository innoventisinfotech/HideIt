# HideIt

Hide apps from your screen, the **taskbar**, and **Alt+Tab** with a global keyboard
shortcut — then bring them back the same way. A lightweight Windows tray utility.

> **What it is:** a convenience / screen-privacy tool. It hides windows instantly.
> **What it is not:** a security tool. Hidden windows are **not** protected or
> encrypted — anyone using the PC can restore them.

## Features

- **Global shortcuts** — assign a shortcut (e.g. `Ctrl+Alt+C`) to any app; press it
  from anywhere to hide/show that app. Hidden windows vanish from the screen, the
  taskbar, and Alt+Tab, and come back focused.
- **Shared shortcuts** — give two or more apps the *same* shortcut and one press
  hides/shows them all together.
- **Floating icon** — optionally show a small, draggable, always-on-top button per
  app. Click it to toggle just that app; it never steals focus from your game/app.
- **Restore all** — a tray command and a **panic hotkey** (default `Ctrl+Alt+`` `,
  configurable in Settings) un-hide everything. Windows are always restored on exit.
- **Launch on click** — if you click a floating icon for an app that isn't running,
  HideIt launches it from its `.exe`.
- **Run at Windows startup** — optional, via the per-user registry Run key.
- **Runs in the tray** — no taskbar clutter; settings live behind the tray icon. A
  first-run welcome panel walks you through the basics.

## Install / Run

### Download

Grab `HideIt.exe` from the [Releases](../../releases) page and run it. It is
**self-contained** — no .NET install required. It starts minimized to the system
tray (look for the HideIt icon near the clock).

> The first time you run an unsigned download, Windows SmartScreen may show
> *"Windows protected your PC."* Click **More info → Run anyway**. See
> [Trust & antivirus](#trust--antivirus) below.

### Usage

1. **Double-click the tray icon** (or right-click → **Settings**).
2. **Add app…** → pick a running app or **Browse for .exe**.
3. Click **Set…** to record a shortcut (needs at least one of Ctrl/Alt/Shift/Win).
4. Optionally tick **Floating icon** for that app.
5. Press your shortcut (or click the floating button) to hide/show.

Config is saved to `%AppData%\HideIt\config.json`. Logs (if any) go to
`%AppData%\HideIt\logs`.

## Build from source

Requires the **.NET 10 SDK** (`dotnet --version` reports `10.x`). No Visual Studio
needed.

```bash
dotnet restore
dotnet build            # verify it compiles
dotnet run              # launch for testing

# single-file, self-contained release exe (with the app icon)
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
# output: bin/Release/net10.0-windows/win-x64/publish/HideIt.exe
```

Replace `Assets/app.ico` with your own icon to rebrand; it drives both the exe and
tray icon.

### Build the installer (optional)

Install [Inno Setup](https://jrsoftware.org/isinfo.php) 6+, publish (above), then:

```bash
iscc installer\HideIt.iss
# output: installer/output/HideIt-Setup-1.0.0.exe
```

The installer is per-user (no admin), adds Start-Menu/optional desktop shortcuts, an
optional "start with Windows" task, and removes the startup entry on uninstall.

### Releasing

A GitHub Actions workflow (`.github/workflows/release.yml`) builds the single-file
exe and attaches it to a Release whenever you push a `v*` tag (e.g. `git tag v1.0.0
&& git push --tags`).

> **One-time setup:** after creating the repo, set `RepoOwner` in `AppInfo.cs` (and
> `MyAppUrl` in `installer/HideIt.iss`) to your GitHub username so the in-app
> "Check for updates" link points to your releases page.

## Known limitations & gotchas

- **Fullscreen-exclusive games** may not hide cleanly (black screen, audio keeps
  playing, or the game forces itself back). **Fix:** run the game in
  **borderless windowed** mode. This is an OS limitation, not a bug.
- **Elevated (admin) apps** can't be hidden unless HideIt also runs as admin.
  HideIt ships as `asInvoker` (normal privileges) in v1.
- **Crash recovery** — if HideIt crashes while windows are hidden, those windows
  stay hidden (the handles are lost). While running, use **Restore all hidden** or
  the panic hotkey. HideIt always restores everything on a clean exit.
- **Multi-window apps** (e.g. several Chrome windows) — all matching windows hide
  and show together.
- **Hotkey conflicts** — if a shortcut is already owned by another app globally,
  registration fails and HideIt shows a non-blocking warning in Settings.

## Privacy

HideIt collects **no data** and makes **no network calls**. Configuration and
optional crash logs are stored locally under `%AppData%\HideIt`.

## Trust & antivirus

HideIt enumerates processes, registers global hotkeys, and hides other apps'
windows from the taskbar/Alt+Tab. That behavior overlaps with what some malware
does, so **SmartScreen warnings and occasional antivirus false positives are
expected** for an unsigned build. The source is open so the behavior is auditable.
Releases are scanned on VirusTotal before publishing.

## License

[MIT](LICENSE) — provided "as is", without warranty of any kind.
