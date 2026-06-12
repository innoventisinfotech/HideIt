# Changelog

All notable changes to HideIt are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-06-12

First release.

### Added
- Tray utility (WPF, .NET 10) that hides/shows apps from the screen, the
  **taskbar**, and **Alt+Tab** on demand.
- Per-app **global keyboard shortcuts**. The same shortcut can be assigned to
  several apps so they hide/show together (group toggle).
- Optional per-app **floating icon button** — draggable, always-on-top, never
  steals focus; left-click toggles that one app, right-click hides the button.
- Settings window with a running-app picker (icons), Browse-for-`.exe`, shortcut
  capture, remove, and per-row floating-icon toggle. Edits persist immediately.
- Tray menu: **Settings**, **Restore all hidden**, **Run at Windows startup**
  (HKCU Run key), **Exit**.
- **Panic hotkey** (default `Ctrl+Alt+`` `) restores every hidden window —
  **configurable** in Settings.
- **First-run onboarding** panel explaining the basics.
- Floating icon on a **closed** app launches it (from its `.exe` path).
- Tray **Check for updates** link to the GitHub releases page.
- Config stored at `%AppData%\HideIt\config.json`.
- Launch hardening: single-instance mutex, global crash logging to
  `%AppData%\HideIt\logs`, and always-restore-on-exit so no window is left
  invisible.
- **Distribution:** GitHub Actions workflow that builds the single-file exe and
  attaches it to a Release on tag push; Inno Setup installer script with
  startup-entry cleanup on uninstall.
