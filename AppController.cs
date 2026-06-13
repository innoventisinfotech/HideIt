using System.Diagnostics;
using System.Windows.Media;
using HideIt.Models;
using HideIt.Services;

namespace HideIt;

/// <summary>One open window offered in the "hide a specific window" picker.</summary>
public sealed record OpenWindow(IntPtr Hwnd, string Title, string ProcessName, ImageSource? Icon);

/// <summary>
/// The brain: holds the live config and ties together hotkeys and the window hider.
/// It never depends on the UI, so hotkeys keep working while the settings window is
/// closed to tray (or while HideIt's own tray icon is hidden).
/// </summary>
public sealed class AppController : IDisposable
{
    public ConfigStore ConfigStore { get; } = new();
    public WindowHider Hider { get; } = new();
    public HotKeyService HotKeys { get; } = new();
    public StartupRegistry Startup { get; } = new();
    public ProcessCatalog Catalog { get; } = new();

    public AppConfig Config { get; private set; } = new();

    /// <summary>The configured panic combo (restores everything), or null if disabled.</summary>
    private HotKeyCombo? PanicCombo => Config.PanicHotKey;

    /// <summary>The configured "show/hide HideIt" combo, or null if disabled.</summary>
    private HotKeyCombo? AppToggleCombo => Config.AppToggleHotKey;

    /// <summary>The configured "show last hidden window" combo, or null if disabled.</summary>
    private HotKeyCombo? ShowLastCombo => Config.ShowLastHotKey;

    /// <summary>Combos that failed to register in the last Reapply (already owned globally).</summary>
    private readonly HashSet<HotKeyCombo> _failedCombos = new();

    /// <summary>
    /// Session-only shortcuts bound to specific window handles (the "assign a shortcut to
    /// this window" feature). Not persisted — handles don't survive a window close/restart.
    /// </summary>
    private readonly Dictionary<HotKeyCombo, List<IntPtr>> _tempWindowBindings = new();

    /// <summary>Raised (UI thread) when a hotkey couldn't be registered.</summary>
    public event Action<HotKeyCombo>? HotKeyRegistrationFailed;

    /// <summary>Raised after config is saved + reapplied, so the UI can refresh.</summary>
    public event Action? ConfigChanged;

    /// <summary>Raised (UI thread) when the user asks to show/hide HideIt's own tray icon.</summary>
    public event Action? ToggleAppVisibilityRequested;

    public AppController()
    {
        HotKeys.Pressed += OnHotKeyPressed;
        HotKeys.RegistrationFailed += OnRegistrationFailed;
    }

    public void Load()
    {
        Config = ConfigStore.Load();
        Reapply();
    }

    public void Save() => ConfigStore.Save(Config);

    /// <summary>Persist, then re-register hotkeys.</summary>
    public void SaveAndReapply()
    {
        Save();
        Reapply();
        ConfigChanged?.Invoke();
    }

    private void Reapply()
    {
        _failedCombos.Clear();
        var combos = Config.Apps
            .Where(a => a.HotKey != null)
            .Select(a => a.HotKey!);
        if (PanicCombo != null)
            combos = combos.Append(PanicCombo);
        if (AppToggleCombo != null)
            combos = combos.Append(AppToggleCombo);
        if (ShowLastCombo != null)
            combos = combos.Append(ShowLastCombo);
        combos = combos.Concat(_tempWindowBindings.Keys);
        HotKeys.RegisterAll(combos);
    }

    private void OnRegistrationFailed(HotKeyCombo combo)
    {
        _failedCombos.Add(combo);
        HotKeyRegistrationFailed?.Invoke(combo);
    }

    /// <summary>True if the app-toggle shortcut is set and registered successfully.</summary>
    public bool AppToggleHotKeyWorks =>
        AppToggleCombo != null && !_failedCombos.Contains(AppToggleCombo);

    // ---- Hotkey handling ----
    private void OnHotKeyPressed(HotKeyCombo combo)
    {
        // A shortcut bound to one or more specific windows (group toggle on those windows).
        if (_tempWindowBindings.TryGetValue(combo, out var hwnds))
        {
            ToggleSpecificWindows(combo, hwnds);
            return;
        }

        // Show/hide HideIt itself — unless the user also assigned it to an app.
        if (AppToggleCombo != null && combo.Equals(AppToggleCombo) && !IsAssignedToApp(combo))
        {
            ToggleAppVisibilityRequested?.Invoke();
            return;
        }

        // Show the most recently individually-hidden window — unless assigned to an app.
        if (ShowLastCombo != null && combo.Equals(ShowLastCombo) && !IsAssignedToApp(combo))
        {
            Hider.ShowLastWindow();
            return;
        }

        // The panic combo restores everything — unless also assigned to an app.
        if (PanicCombo != null && combo.Equals(PanicCombo) && !IsAssignedToApp(combo))
        {
            Hider.ShowAll();
            return;
        }

        ToggleGroup(combo);
    }

    private bool IsAssignedToApp(HotKeyCombo combo) =>
        Config.Apps.Any(a => combo.Equals(a.HotKey));

    /// <summary>§6.1 group toggle: if every running member is hidden, show all; else hide all.</summary>
    public void ToggleGroup(HotKeyCombo combo)
    {
        var group = Config.Apps
            .Where(a => combo.Equals(a.HotKey) && IsRunning(a.ProcessName))
            .ToList();
        if (group.Count == 0) return;

        bool allHidden = group.All(a => Hider.IsHidden(a.ProcessName));
        foreach (var a in group)
        {
            if (allHidden) Hider.Show(a.ProcessName);
            else Hider.Hide(a.ProcessName);
        }
    }

    public void ShowAllHidden() => Hider.ShowAll();

    private static bool IsRunning(string processName)
    {
        var ps = Process.GetProcessesByName(processName);
        bool any = ps.Length > 0;
        foreach (var p in ps) p.Dispose();
        return any;
    }

    /// <summary>Update the panic shortcut (null disables it) and re-register.</summary>
    public void SetPanicHotKey(HotKeyCombo? combo)
    {
        Config.PanicHotKey = combo;
        SaveAndReapply();
    }

    /// <summary>Update the show/hide-HideIt shortcut (null disables it) and re-register.</summary>
    public void SetAppToggleHotKey(HotKeyCombo? combo)
    {
        Config.AppToggleHotKey = combo;
        SaveAndReapply();
    }

    /// <summary>Update the "show last hidden window" shortcut (null disables it) and re-register.</summary>
    public void SetShowLastHotKey(HotKeyCombo? combo)
    {
        Config.ShowLastHotKey = combo;
        SaveAndReapply();
    }

    // ---- Hide a specific window (the picker) ----

    /// <summary>All open "real" windows except HideIt's own, for the window picker.</summary>
    public List<OpenWindow> GetOpenWindows()
    {
        uint selfPid = (uint)Environment.ProcessId;
        var list = new List<OpenWindow>();

        foreach (var w in Native.GetAllRealWindows())
        {
            if (w.Pid == selfPid) continue;

            string procName = "";
            string? exe = null;
            try
            {
                using var p = Process.GetProcessById((int)w.Pid);
                procName = p.ProcessName;
                try { exe = p.MainModule?.FileName; } catch { /* denied / bitness */ }
            }
            catch { /* process vanished */ }

            var title = string.IsNullOrWhiteSpace(w.Title) ? procName : w.Title;
            list.Add(new OpenWindow(w.Hwnd, title, procName, Catalog.GetIconFor(exe)));
        }

        return list
            .OrderBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Hide the given specific windows by handle.</summary>
    public void HideSpecificWindows(IEnumerable<IntPtr> handles)
    {
        foreach (var h in handles)
            Hider.HideWindow(h);
    }

    /// <summary>
    /// Bind a session-only shortcut to specific windows. Returns false if the combo
    /// could not be registered (already owned globally).
    /// </summary>
    public bool AddTempWindowBinding(HotKeyCombo combo, IReadOnlyList<IntPtr> handles)
    {
        _tempWindowBindings[combo] = handles.ToList();
        Reapply();
        return !_failedCombos.Contains(combo);
    }

    /// <summary>Group-toggle a set of specific windows; drops the binding once all have closed.</summary>
    private void ToggleSpecificWindows(HotKeyCombo combo, List<IntPtr> handles)
    {
        var alive = handles.Where(Native.IsWindow).ToList();
        if (alive.Count == 0)
        {
            _tempWindowBindings.Remove(combo);
            Reapply();
            return;
        }

        bool allHidden = alive.All(Hider.IsWindowHidden);
        foreach (var h in alive)
        {
            if (allHidden) Hider.ShowSpecificWindow(h);
            else Hider.HideWindow(h);
        }
    }

    public void Dispose()
    {
        HotKeys.Dispose();
        Hider.ShowAll(); // never leave a window invisible on exit
    }
}
