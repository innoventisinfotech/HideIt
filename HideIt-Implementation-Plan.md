# HideIt — Implementation Plan (Windows, WPF)

A tray utility that lets a user pick apps, assign global keyboard shortcuts to them, and hide/show those apps on demand. Hiding removes a window from the screen, the **taskbar**, and **Alt+Tab**. The same shortcut can be assigned to multiple apps so they all hide/show together. Each app can optionally show a small, draggable, always-on-top **floating icon button** that toggles that one app when clicked.

This document is the build spec. Implement it in the ordered milestones at the end; each milestone is independently testable.

---

## 1. Goal & behavior summary

- A settings window (in the system tray) where the user manages a list of apps.
- For each app the user sets: a **keyboard shortcut** and whether to show a **floating icon**.
- Pressing a shortcut toggles every app assigned to it:
  - If **all** running apps in that group are currently hidden → **show** them all.
  - Otherwise → **hide** them all.
- A floating icon, if enabled for an app, toggles just that single app on left-click.
- Hidden windows are gone from screen, taskbar, and Alt+Tab. Toggling brings them back and focuses them.
- Runs in the tray; optional "run at Windows startup".

### Out of scope (v1)
- macOS (OS restrictions prevent removing other apps from the Dock/Cmd+Tab; Windows-only for now).
- Installer (single-file `.exe` publish is enough for v1; Inno Setup/WiX can come later).

---

## 2. Tech stack (pinned)

| Concern | Choice |
|---|---|
| Language | C# |
| Runtime | **.NET 10** (current LTS, supported to Nov 2028) |
| UI | **WPF** |
| MVVM helpers | `CommunityToolkit.Mvvm` (source-generated `[ObservableProperty]`, `[RelayCommand]`) |
| Tray icon | `H.NotifyIcon.Wpf` |
| Win32 access | Hand-written P/Invoke (one `Native.cs`) |
| Config | `System.Text.Json` → file in `%AppData%` |
| App icons | `System.Drawing` (`Icon.ExtractAssociatedIcon`) — enable `<UseWindowsForms>` is NOT needed; use `System.Drawing.Common` |

Add NuGets with `dotnet add package CommunityToolkit.Mvvm`, `dotnet add package H.NotifyIcon.Wpf`, `dotnet add package System.Drawing.Common`. Use latest stable versions for .NET 10.

### Project file (`HideIt.csproj`)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>HideIt</AssemblyName>
    <RootNamespace>HideIt</RootNamespace>
    <ApplicationIcon>Assets\app.ico</ApplicationIcon>
    <Version>1.0.0</Version>
  </PropertyGroup>
</Project>
```

### Prerequisites
- Windows 10/11, 64-bit.
- .NET 10 SDK installed (`dotnet --version` should report 10.x).
- Buildable entirely from the CLI: `dotnet build` / `dotnet run` / `dotnet publish`. No Visual Studio required.

---

## 3. Project structure

```
HideIt/
├─ HideIt.csproj
├─ App.xaml / App.xaml.cs            # startup, owns services + tray, no StartupUri (start hidden to tray)
├─ Assets/
│  └─ app.ico                        # tray + exe icon (provide a placeholder, replaceable)
├─ Models/
│  ├─ AppEntry.cs                    # one configured app (process, shortcut, icon prefs)
│  ├─ HotKeyCombo.cs                 # modifiers + key value object + formatting
│  └─ AppConfig.cs                   # root config (list of AppEntry + global prefs)
├─ Services/
│  ├─ Native.cs                      # all P/Invoke + constants + window enumeration helpers
│  ├─ WindowHider.cs                 # hide/show windows of a process; tracks hidden handles
│  ├─ HotKeyService.cs               # global hotkey registration via HwndSource; grouping
│  ├─ ProcessCatalog.cs             # list running apps w/ main windows + icons; icon extraction
│  ├─ ConfigStore.cs                 # load/save AppConfig as JSON in %AppData%
│  └─ StartupRegistry.cs             # toggle "run at Windows startup" (HKCU Run key)
├─ ViewModels/
│  ├─ MainViewModel.cs               # settings list, add/remove, save
│  └─ AddAppViewModel.cs             # running-process picker / browse exe
├─ Views/
│  ├─ MainWindow.xaml(.cs)           # settings UI
│  ├─ AddAppDialog.xaml(.cs)         # pick an app to add
│  ├─ HotKeyCaptureControl.xaml(.cs) # captures a key combo
│  └─ FloatingIconWindow.xaml(.cs)   # the per-app floating button
└─ AppController.cs                  # ties config↔hotkeys↔hider↔floating icons together
```

`AppController` is the brain: it holds the live `AppConfig`, the `WindowHider`, the `HotKeyService`, and the dictionary of open `FloatingIconWindow`s. The UI talks to the controller; the controller never depends on the UI. This keeps hotkeys and floating icons alive even when the settings window is closed to tray.

---

## 4. Data model & config schema

### `HotKeyCombo`
Normalized modifier bitmask (our own, independent of Win32 values) + virtual key code.

```
[Flags] enum Mod { None=0, Alt=1, Ctrl=2, Shift=4, Win=8 }

class HotKeyCombo {
    Mod Modifiers;     // at least one modifier required
    int VirtualKey;    // Win32 VK code (from KeyInterop.VirtualKeyFromKey)
    string Display();  // e.g. "Ctrl + Alt + C"
    bool Equals/GetHashCode  // value equality so combos can group
}
```

### `AppEntry`
```
class AppEntry {
    string Id;               // GUID
    string ProcessName;      // e.g. "chrome" (no .exe), case-insensitive match
    string DisplayName;      // e.g. "Google Chrome"
    string? ExePath;         // for icon + launching apps not yet running
    HotKeyCombo? HotKey;     // null = unassigned
    bool ShowFloatingIcon;   // default false
    double IconX, IconY;     // last floating-icon position
}
```

### `AppConfig` (root, serialized)
```
class AppConfig {
    List<AppEntry> Apps;
    bool RunAtStartup;
}
```

### On-disk JSON
Path: `%AppData%\HideIt\config.json`. Example:

```json
{
  "apps": [
    {
      "id": "0e9b...",
      "processName": "chrome",
      "displayName": "Google Chrome",
      "exePath": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
      "hotKey": { "modifiers": 6, "virtualKey": 67 },
      "showFloatingIcon": true,
      "iconX": 1200, "iconY": 80
    }
  ],
  "runAtStartup": false
}
```

`modifiers: 6` = Ctrl(2)|Shift(4)... (use the `Mod` flags above). `virtualKey: 67` = `C`.

---

## 5. Subsystem specs

### 5.1 `Native.cs` — Win32 interop

Required P/Invoke signatures (declare with `LibraryImport`/source-gen or `DllImport`):

```
// Hotkeys
[DllImport("user32.dll")] bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
[DllImport("user32.dll")] bool UnregisterHotKey(IntPtr hWnd, int id);

// Window show/hide + focus
[DllImport("user32.dll")] bool ShowWindow(IntPtr hWnd, int nCmdShow);          // SW_HIDE=0, SW_SHOW=5, SW_SHOWNA=8
[DllImport("user32.dll")] bool SetForegroundWindow(IntPtr hWnd);

// Extended styles (use Ptr variants; provide 32-bit fallback selecting on IntPtr.Size)
IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);   // GWL_EXSTYLE = -20
IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

// Enumeration
[DllImport("user32.dll")] bool EnumWindows(EnumWindowsProc cb, IntPtr l);
[DllImport("user32.dll")] uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
[DllImport("user32.dll")] bool IsWindowVisible(IntPtr hWnd);
[DllImport("user32.dll")] IntPtr GetWindow(IntPtr hWnd, uint cmd);             // GW_OWNER = 4
[DllImport("user32.dll")] int GetWindowTextLength(IntPtr hWnd);
[DllImport("dwmapi.dll")] int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int val, int size); // DWMWA_CLOAKED = 14
```

Constants:
```
GWL_EXSTYLE        = -20
WS_EX_TOOLWINDOW   = 0x00000080   // removes from taskbar AND Alt+Tab
WS_EX_APPWINDOW    = 0x00040000   // forces onto taskbar
WS_EX_NOACTIVATE   = 0x08000000   // for floating icon: don't steal focus
SW_HIDE=0, SW_SHOW=5, SW_SHOWNA=8
GW_OWNER = 4
MOD_ALT=0x1, MOD_CONTROL=0x2, MOD_SHIFT=0x4, MOD_WIN=0x8, MOD_NOREPEAT=0x4000
DWMWA_CLOAKED = 14
```

**`GetWindowLongPtr` 32-bit fallback:** on `IntPtr.Size == 4` use entry point `GetWindowLong`/`SetWindowLong`; on 64-bit use `GetWindowLongPtrW`/`SetWindowLongPtrW`. Wrap in `GetExStyle(hwnd)` / `SetExStyle(hwnd, value)` helpers.

**Helper: `IEnumerable<IntPtr> GetTopLevelWindows(string processName)`**
1. Get pids: `Process.GetProcessesByName(processName)` → set of pids.
2. `EnumWindows`; for each hwnd: `GetWindowThreadProcessId` → if pid in set AND `IsRealAppWindow(hwnd)` → yield.

**Helper: `bool IsRealAppWindow(IntPtr hwnd)`** (an "Alt+Tab-able" window):
- `IsWindowVisible(hwnd)` is true, AND
- `GetWindow(hwnd, GW_OWNER) == IntPtr.Zero` (no owner), AND
- `GetWindowTextLength(hwnd) > 0` (has a title), AND
- `(GetExStyle(hwnd) & WS_EX_TOOLWINDOW) == 0`, AND
- not cloaked: `DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out v, 4)` returns `v == 0`.

> Note: when a window is already hidden, `IsWindowVisible` is false — that's fine, because Show() uses **stored handles**, not enumeration.

### 5.2 `WindowHider.cs`

Tracks, per process name, the windows it hid and their original extended style so it can restore exactly.

```
record HiddenWin(IntPtr Hwnd, IntPtr OriginalExStyle);

class WindowHider {
    // processName(lower) -> list of hidden windows
    Dictionary<string, List<HiddenWin>> _hidden;  // OrdinalIgnoreCase

    bool IsHidden(string processName);             // has stored handles
    void Hide(string processName);                 // enumerate + hide all real windows
    void Show(string processName);                 // restore stored handles, focus first
    void Toggle(string processName) => IsHidden ? Show : Hide;
    void ShowAll();                                // restore everything (panic / on exit)
}
```

**Hide(processName):**
```
var wins = Native.GetTopLevelWindows(processName).ToList();
var list = new List<HiddenWin>();
foreach hwnd in wins:
    ex = Native.GetExStyle(hwnd);
    Native.SetExStyle(hwnd, (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
    Native.ShowWindow(hwnd, SW_HIDE);
    list.Add(new HiddenWin(hwnd, ex));
if list.Count > 0: _hidden[key] = list;
```

**Show(processName):**
```
if not _hidden.TryGetValue(key, out list): return;
foreach (hwnd, originalEx) in list:
    Native.SetExStyle(hwnd, originalEx);     // restore exact original style
    Native.ShowWindow(hwnd, SW_SHOW);
Native.SetForegroundWindow(list[0].Hwnd);
_hidden.Remove(key);
```

**Important:** on app **exit**, call `ShowAll()` so nothing is left invisible. Also expose `ShowAll()` to a tray "Restore all hidden" command and a global panic hotkey (see 5.7). If the process crashes while windows are hidden, those handles are lost and the windows stay hidden until reopened — document this; mitigate with the always-available "Restore all" while running.

### 5.3 `HotKeyService.cs`

WPF has no `WndProc` on a Window by default. Use a dedicated **message-only** `HwndSource` so hotkeys live independently of the settings window.

```
class HotKeyService : IDisposable {
    HwndSource _source;
    Dictionary<int, HotKeyCombo> _idToCombo;  // id -> combo
    int _nextId = 1;
    public event Action<HotKeyCombo> Pressed;

    ctor:
        var p = new HwndSourceParameters("HideIt.HotKeyWindow") {
            Width = 0, Height = 0,
            ParentWindow = new IntPtr(-3)   // HWND_MESSAGE (message-only)
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);

    void RegisterAll(IEnumerable<HotKeyCombo> combos):
        UnregisterAll();
        foreach distinct combo:
            uint mods = ToWin32Mods(combo.Modifiers) | MOD_NOREPEAT;
            uint vk   = (uint)combo.VirtualKey;
            int id    = _nextId++;
            if Native.RegisterHotKey(_source.Handle, id, mods, vk):
                _idToCombo[id] = combo;
            // if it fails (already taken by another app), surface a non-fatal warning

    void UnregisterAll():
        foreach id: Native.UnregisterHotKey(_source.Handle, id);
        _idToCombo.Clear(); _nextId = 1;

    IntPtr WndProc(hwnd, msg, w, l, ref handled):
        const WM_HOTKEY = 0x0312;
        if msg == WM_HOTKEY and _idToCombo.TryGetValue(w.ToInt32(), out combo):
            Pressed?.Invoke(combo); handled = true;
        return IntPtr.Zero;

    ToWin32Mods(Mod m): map Alt→MOD_ALT, Ctrl→MOD_CONTROL, Shift→MOD_SHIFT, Win→MOD_WIN
}
```

Register each **unique** combo once even if several apps share it (dedupe by `HotKeyCombo` value equality).

### 5.4 `ProcessCatalog.cs`

For the "Add app" picker and for icons.

```
record RunningApp(string ProcessName, string DisplayName, string? ExePath, ImageSource Icon);

class ProcessCatalog {
    IEnumerable<RunningApp> GetRunningAppsWithWindows():
        // Process.GetProcesses() where MainWindowHandle != 0 and MainWindowTitle not empty,
        // distinct by ProcessName, with icon + exe path (try p.MainModule.FileName in try/catch)

    ImageSource GetIconFor(string? exePath):
        // Icon.ExtractAssociatedIcon(exePath) -> convert to BitmapSource (Imaging.CreateBitmapSourceFromHIcon)
        // fallback to a bundled default icon on any exception (access denied, 32/64 mismatch)
}
```

`p.MainModule.FileName` throws for some processes (elevated, bitness mismatch) — always wrap and fall back gracefully.

### 5.5 `ConfigStore.cs`

```
class ConfigStore {
    string Path => %AppData%\HideIt\config.json
    AppConfig Load();   // create default if missing/corrupt
    void Save(AppConfig);  // create dir if needed; write indented JSON
}
```

Use `System.Text.Json` with `WriteIndented = true`. Serialize `Mod` as its integer value.

### 5.6 `StartupRegistry.cs`

```
const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
const string ValueName = "HideIt";
bool IsEnabled();
void SetEnabled(bool on):
    on  -> set HKCU Run\HideIt = "\"" + Environment.ProcessPath + "\""
    off -> delete value if present
```

### 5.7 `FloatingIconWindow.xaml`

A small always-on-top button showing the app's icon.

XAML window: `WindowStyle="None"`, `AllowsTransparency="True"`, `Background="Transparent"`, `Topmost="True"`, `ShowInTaskbar="False"`, `SizeToContent="WidthAndHeight"`, `ResizeMode="NoResize"`. Content = `Image` (app icon, ~40×40) inside a rounded `Border`.

In `OnSourceInitialized`, add `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` to its extended style so it stays out of Alt+Tab and never steals focus from the game/app.

Behavior:
- **Left mouse down** → record start point.
- **Move** beyond a few px → drag the window (set `Left`/`Top`); mark as "moved".
- **Mouse up**: if not moved → raise `Clicked` (controller toggles that one app). If moved → persist new `Left`/`Top` into the `AppEntry` and save config.
- **Right-click** → context menu "Hide this button" → sets `ShowFloatingIcon=false` for that entry (controller closes the window, save config).

The controller owns a `Dictionary<string entryId, FloatingIconWindow>`; it opens/closes these whenever `ShowFloatingIcon` changes or config loads.

### 5.8 Tray (`App.xaml` + H.NotifyIcon)

`App` starts with **no main window shown** (no `StartupUri`). It creates the `AppController`, loads config, applies hotkeys + floating icons, and shows a `TaskbarIcon` (H.NotifyIcon) with a context menu:
- **Settings** → show/focus `MainWindow`.
- **Restore all hidden** → `WindowHider.ShowAll()`.
- **Run at Windows startup** (checkable) → `StartupRegistry`.
- **Exit** → `WindowHider.ShowAll()`, dispose services, shut down.

Closing the `MainWindow` hides it to tray (cancel close, `Hide()`), it does not exit the app. Also register a **panic hotkey** (e.g. default `Ctrl+Alt+\``) that calls `ShowAll()` — configurable later, hardcoded acceptable for v1.

---

## 6. Core business rules

1. **Shared-shortcut grouping.** When a hotkey fires, find all `AppEntry` whose `HotKey` equals that combo. Among those whose process is currently running:
   - If **every** one is hidden (`WindowHider.IsHidden`) → `Show` each.
   - Else → `Hide` each.
   Reading live state from `WindowHider` (instead of a stored bool) keeps the group in sync even after a floating-icon single-toggle.
2. **Floating icon = single-app toggle.** Click toggles only that entry's process via `WindowHider.Toggle`.
3. **At least one modifier required** when capturing a shortcut (reject bare keys to avoid hijacking normal typing).
4. **Unassigned apps** (no hotkey, no icon) are allowed but do nothing until configured.
5. **Re-apply on any config change.** After add/remove/edit: save config, `HotKeyService.RegisterAll(distinct combos)`, reconcile floating icon windows.

---

## 7. UI / Views

### `MainWindow` (Settings)
- A `DataGrid` (or `ItemsControl`) bound to `MainViewModel.Apps` (`ObservableCollection<AppEntryVm>`), columns:
  - **Icon** (image), **App name**, **Shortcut** (text + a "Set…" button opening `HotKeyCaptureControl`), **Floating icon** (checkbox bound to `ShowFloatingIcon`).
- Buttons: **Add app…** (opens `AddAppDialog`), **Remove** (selected), **Restore all hidden**.
- Checkbox: **Run at Windows startup**.
- Changes commit immediately to config (save on each edit) — simplest, no separate Apply.

### `AddAppDialog`
- Tab/list of **running apps** (from `ProcessCatalog.GetRunningAppsWithWindows()`), each with icon + name; user selects one.
- **Browse…** button → `OpenFileDialog` (*.exe) to add an app that isn't currently running (ProcessName = filename without extension).
- OK returns a new `AppEntry`.

### `HotKeyCaptureControl`
- A focusable box: "Press a shortcut…". Handle `PreviewKeyDown`:
  - `var mods = Keyboard.Modifiers;` (WPF `ModifierKeys` includes **Windows**).
  - `var key = e.Key == Key.System ? e.SystemKey : e.Key;` (Alt combos report `Key.System`).
  - Ignore pure modifier keys (`LeftCtrl`, `LeftAlt`, `LeftShift`, `LWin`, etc.) as the main key.
  - Require `mods != None`; build `HotKeyCombo { Modifiers, VirtualKey = KeyInterop.VirtualKeyFromKey(key) }`.
  - Show `combo.Display()`; `e.Handled = true`.

---

## 8. Edge cases & gotchas (must handle / document)

- **Fullscreen exclusive games**: may not hide cleanly (black screen / audio continues / forces itself back). Fix: run the game in **borderless windowed** mode. Document this in README; it's an OS limitation, not a bug.
- **Elevated apps**: HideIt can't manipulate windows of higher-integrity (admin) processes unless HideIt itself runs as admin. Document. (v1 stays `asInvoker`.)
- **Crash recovery**: if HideIt crashes while windows are hidden, those windows stay hidden (handles lost). Mitigate with the always-available "Restore all hidden" tray command + panic hotkey while running. Always `ShowAll()` on clean exit.
- **Multi-window apps** (e.g. several Chrome windows): `Hide` enumerates and hides **all** matching real windows; `Show` restores **all** tracked handles.
- **Cloaked/UWP windows**: filtered by the `DWMWA_CLOAKED` check. Basic UWP support may be imperfect; acceptable for v1.
- **Hotkey conflicts**: `RegisterHotKey` fails if the combo is already owned globally. Surface a non-blocking warning in the UI; don't crash.
- **Same shortcut, multiple apps**: register the combo once; the controller fans out to all entries sharing it.
- **DPI**: WPF is DPI-aware by default; ensure floating icon positions are stored in device-independent units (WPF `Left`/`Top` already are).

---

## 9. Build, run, package

```bash
# from the HideIt/ folder
dotnet restore
dotnet build                         # verify it compiles after each milestone
dotnet run                           # launch for manual testing

# single-file, self-contained release exe with the app icon
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
# output: bin/Release/net10.0-windows/win-x64/publish/HideIt.exe
```

Replace `Assets/app.ico` with the real icon; it drives both the exe icon and the tray icon.

---

## 10. Milestones (build in this order; each is independently testable)

**M0 — Scaffold.** WPF .NET 10 project; add the three NuGets; app starts to tray (no window shown), tray menu with **Settings** + **Exit** works. `dotnet build` clean.

**M1 — Window hiding core.** Implement `Native.cs` + `WindowHider`. Add a temporary test button (or tray command) that hides/shows Notepad by process name. **Verify**: Notepad disappears from screen, taskbar, and Alt+Tab, and comes back focused. Restore-on-exit works.

**M2 — Global hotkeys.** Implement `HotKeyService` with the message-only `HwndSource`. Register one hardcoded combo; log on press while focused on another app. **Verify**: fires globally, including from a game/browser; `MOD_NOREPEAT` prevents auto-repeat.

**M3 — Config.** `AppEntry`/`HotKeyCombo`/`AppConfig` + `ConfigStore` load/save to `%AppData%\HideIt\config.json`. Round-trip test.

**M4 — Settings UI.** `MainWindow` + `MainViewModel`; `AddAppDialog` (running-process picker with icons + Browse exe); Remove; `HotKeyCaptureControl`; per-row floating-icon checkbox. Edits persist to config. (Hotkeys/icons not wired to behavior yet.)

**M5 — Wire shortcuts.** `AppController` registers all distinct combos and implements the **group toggle** rule (§6.1). **Verify**: one shortcut hides/shows multiple assigned apps together.

**M6 — Floating icons.** `FloatingIconWindow` with no-activate/no-alttab styles; create/destroy per `ShowFloatingIcon`; left-click toggles that app; drag + persist position; right-click "Hide this button". **Verify**: clicking the floating icon hides/shows just that app without stealing focus.

**M7 — Tray polish.** Tray: Settings, **Restore all hidden**, **Run at Windows startup** (registry), Exit. Panic hotkey → `ShowAll()`. Real `app.ico`.

**M8 — Package.** Single-file self-contained publish; README with build/run instructions and the borderless-game note.

---

## 11. Acceptance checklist

- [ ] Add Chrome + a game + Discord via the picker (icons shown).
- [ ] Assign `Ctrl+Alt+C` to Chrome alone → toggles Chrome; gone from taskbar & Alt+Tab.
- [ ] Assign the **same** `Ctrl+Alt+H` to two apps → one press hides both, next press shows both.
- [ ] Enable a floating icon for one app → click hides/shows only it; drag repositions and persists; focus on the active app is not stolen.
- [ ] Disable a floating icon → its button disappears.
- [ ] "Restore all hidden" un-hides everything; clean exit restores everything.
- [ ] "Run at Windows startup" adds/removes the HKCU Run entry.
- [ ] Config survives restart (`%AppData%\HideIt\config.json`).
- [ ] Borderless-windowed game hides correctly; fullscreen-exclusive limitation documented.
```

---

## 12. Launch readiness (shipping to the public, free)

Everything above gets the app *built*. This section gets it *distributable* — what's required before strangers can safely install it. These are additive workstreams, mostly independent of M0–M8.

### 12.1 Trust: SmartScreen & antivirus (the #1 priority)

HideIt enumerates processes, registers global hotkeys, and hides other apps' windows from the taskbar/Alt+Tab. That behavioral profile overlaps heavily with keyloggers/malware, so **expect SmartScreen warnings and antivirus false positives**. Unsigned, every new user sees the blue "Windows protected your PC" prompt and must click "More info → Run anyway"; some AVs may quarantine it. This determines whether the app spreads or gets deleted on sight.

**Code-signing options (current landscape, mid-2026):**

| Option | Cost | Notes |
|---|---|---|
| **Azure Artifact Signing** (formerly *Trusted Signing*) | ~$9.99/mo (5,000 signatures) | Modern, no hardware token. **BUT** individual sign-up is currently limited to US/Canada; EU/UK is organizations only. Not available to an individual in India yet — would require registering as an organization. |
| **Traditional OV code-signing cert** (DigiCert, Sectigo, SSL2BUY, etc.) | ~$200–550/yr | Works anywhere. Now requires the private key in an HSM / hardware token (or Azure Key Vault). Removes the SmartScreen warning immediately. |
| **Ship unsigned + open-source** | Free | Common for free indie utilities. Users tolerate SmartScreen for OSS; reputation builds over time. Start here, upgrade to signing once there's traction. |

**Recommended path for a free app from an individual:** start **open-source + unsigned** on GitHub, then add signing (OV cert, or Azure route after incorporating) once it gains users.

**Required regardless:**
- Run **every release through VirusTotal** before publishing; if reputable engines flag it, submit false-positive reports to those vendors.
- Keep the codebase open so the behavior is auditable — this materially reduces "is this a virus?" friction.

### 12.2 Engineering hardening for public use

Add these on top of M0–M8 (fold into M7/M8):

- **Single-instance lock** — a named `Mutex` at startup; if already running, focus the existing instance and exit. Prevents double hotkey registration.
- **Global crash logging** — handle `AppDomain.CurrentDomain.UnhandledException` and `Application.DispatcherUnhandledException`; write stack traces to `%AppData%\HideIt\logs\`. Essential for supporting users you can't see.
- **Clean uninstall** — remove the `HKCU\...\Run` startup entry **and** call `WindowHider.ShowAll()` so no window is left invisible. A stranded hidden window with no recovery path is the worst first impression.
- **First-run onboarding** — a one-time panel: how to add an app, how to set a shortcut, and the borderless-game caveat. Strangers won't read a README.
- **Graceful "target not running"** — pressing a shortcut for a closed app should do nothing quietly (optionally offer to launch it from `ExePath`).
- **Self-contained publish** (already in M8) so users don't need .NET installed.

### 12.3 Distribution

- **Hosting:** **GitHub Releases** — free, gives you a built-in issue tracker for bug reports, and signals openness. Avoid the **Microsoft Store** for v1: apps that hook global hotkeys and manipulate *other* apps' windows frequently fail Store certification. Sideload/GitHub is the permissive path.
- **Installer (optional):** **Inno Setup** (free) for a friendly install/uninstall flow users expect. A bare single `.exe` also works for v1.
- **Auto-update:** **Velopack** (free, modern .NET desktop updater) so users don't manually re-download. Otherwise add a "check for updates" link that opens the GitHub releases page.
- **Versioning:** semantic version in the csproj + a `CHANGELOG.md`; tag each GitHub release.

### 12.4 Legal & licensing

- **License:** **MIT** if open-source — permissive, encourages trust/contribution, and includes the critical **"provided as-is, without warranty"** clause (important for a tool that hides windows; shields you from "it lost my window" claims). Use a freeware EULA instead only if you keep it closed-source.
- **Privacy:** if the app collects nothing, state "HideIt collects no data and makes no network calls" in the README. The moment you add crash reporting or analytics, you need a real privacy policy.
- **Name/trademark:** confirm "HideIt" (or final name) isn't already a trademarked/published app before branding around it.

### 12.5 Honest positioning

Market HideIt as a **convenience/privacy-of-screen tool** ("hide windows instantly with a shortcut"), **not a security tool**. Hidden windows are not protected or encrypted — anyone can restore them. Overselling it as "securely hide" invites disappointed users and the wrong kind of scrutiny.

### 12.6 Launch checklist

- [ ] Decide signing strategy (unsigned+OSS to start, or buy OV cert).
- [ ] Latest build scanned on VirusTotal; false positives reported.
- [ ] Single-instance mutex implemented.
- [ ] Global crash logging to `%AppData%\HideIt\logs\`.
- [ ] Uninstall removes startup entry + restores hidden windows.
- [ ] First-run onboarding panel.
- [ ] `LICENSE` (MIT) + `README.md` + `CHANGELOG.md` in repo.
- [ ] "No data collected" statement (or privacy policy if telemetry added).
- [ ] GitHub repo + Releases set up; (optional) Inno Setup installer; (optional) Velopack auto-update.
- [ ] App name/trademark checked.
