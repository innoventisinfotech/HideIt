# Changelog

All notable changes to HideIt are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [1.0.1] - 2026-06-13

### Added
- **Hide HideIt itself** — a tray command and a configurable global shortcut
  (default `Ctrl+Alt+Shift+H`) hide HideIt's own tray icon, including from the
  notification-area overflow. HideIt keeps running; the shortcut brings it back.
  Refuses to hide the icon unless a working show/hide shortcut is registered.
- **Hide a specific window** — a "Hide a window…" picker (tray + Settings) lists
  every open window so you can hide individual ones instead of the whole process,
  e.g. just one of several Chrome profile windows.
- **Assign a temporary shortcut to specific windows** from the picker: tick one or
  more windows, capture a combo, and that shortcut toggles exactly those windows
  without opening HideIt. Session-only (handles don't survive a window close).
- **Show last hidden window** shortcut (default `Ctrl+Alt+Shift+S`) and **Restore
  all** bring individually-hidden windows back.
- **Create Desktop / Start Menu shortcuts** from the tray ("Create shortcut") and
  from Settings — handy for the portable single-file build.

### Removed
- The per-app **floating icon** feature. (Old `showFloatingIcon`/`iconX`/`iconY`
  config fields are ignored on load and dropped on the next save.)

## [1.0.0] - 2026-06-12

First release.

### Added
- Tray utility (WPF, .NET 10) that hides/shows apps from the screen, the
  **taskbar**, and **Alt+Tab** on demand.
- Per-app **global keyboard shortcuts**. The same shortcut can be assigned to
  several apps so they hide/show together (group toggle).
- Optional per-app **floating icon button** to toggle a single app.
- Settings window with a running-app picker (icons), Browse-for-`.exe`, shortcut
  capture, and remove. Edits persist immediately.
- Tray menu: **Settings**, **Restore all hidden**, **Run at Windows startup**
  (HKCU Run key), **Exit**.
- **Panic hotkey** (default `Ctrl+Alt+`` `) restores every hidden window —
  **configurable** in Settings.
- **First-run onboarding** panel explaining the basics.
- Tray **Check for updates** link to the GitHub releases page.
- Config stored at `%AppData%\HideIt\config.json`.
- Launch hardening: single-instance mutex, global crash logging to
  `%AppData%\HideIt\logs`, and always-restore-on-exit so no window is left
  invisible.
- **Distribution:** GitHub Actions workflow that builds the single-file exe and
  attaches it to a Release on tag push; Inno Setup installer script with
  startup-entry cleanup on uninstall.
