using System.Diagnostics;
using System.IO;
using HideIt.Models;
using HideIt.Services;
using HideIt.Views;

namespace HideIt;

/// <summary>
/// The brain: holds the live config and ties together hotkeys, the window hider and
/// the floating icons. It never depends on the UI, so hotkeys and floating buttons keep
/// working while the settings window is closed to tray.
/// </summary>
public sealed class AppController : IDisposable
{
    public ConfigStore ConfigStore { get; } = new();
    public WindowHider Hider { get; } = new();
    public HotKeyService HotKeys { get; } = new();
    public StartupRegistry Startup { get; } = new();
    public ProcessCatalog Catalog { get; } = new();

    public AppConfig Config { get; private set; } = new();

    private readonly Dictionary<string, FloatingIconWindow> _floating = new();

    /// <summary>The configured panic combo (restores everything), or null if disabled.</summary>
    private HotKeyCombo? PanicCombo => Config.PanicHotKey;

    /// <summary>Raised (UI thread) when a hotkey couldn't be registered.</summary>
    public event Action<HotKeyCombo>? HotKeyRegistrationFailed;

    /// <summary>Raised after config is saved + reapplied, so the UI can refresh.</summary>
    public event Action? ConfigChanged;

    public AppController()
    {
        HotKeys.Pressed += OnHotKeyPressed;
        HotKeys.RegistrationFailed += c => HotKeyRegistrationFailed?.Invoke(c);
    }

    public void Load()
    {
        Config = ConfigStore.Load();
        Reapply();
    }

    public void Save() => ConfigStore.Save(Config);

    /// <summary>Persist, then re-register hotkeys and reconcile floating icons.</summary>
    public void SaveAndReapply()
    {
        Save();
        Reapply();
        ConfigChanged?.Invoke();
    }

    private void Reapply()
    {
        var combos = Config.Apps
            .Where(a => a.HotKey != null)
            .Select(a => a.HotKey!);
        if (PanicCombo != null)
            combos = combos.Append(PanicCombo);
        HotKeys.RegisterAll(combos);
        ReconcileFloatingIcons();
    }

    // ---- Hotkey handling ----
    private void OnHotKeyPressed(HotKeyCombo combo)
    {
        // The panic combo restores everything — unless the user also assigned it to an app,
        // in which case it behaves like a normal group toggle.
        if (PanicCombo != null && combo.Equals(PanicCombo) && !Config.Apps.Any(a => combo.Equals(a.HotKey)))
        {
            Hider.ShowAll();
            return;
        }
        ToggleGroup(combo);
    }

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

    public void ToggleSingle(AppEntry entry)
    {
        // If it isn't running, a click on the floating icon launches it instead.
        if (!IsRunning(entry.ProcessName))
        {
            TryLaunch(entry);
            return;
        }
        Hider.Toggle(entry.ProcessName);
    }

    private static void TryLaunch(AppEntry entry)
    {
        if (string.IsNullOrEmpty(entry.ExePath) || !File.Exists(entry.ExePath)) return;
        try
        {
            Process.Start(new ProcessStartInfo(entry.ExePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.LogException($"Launch {entry.ExePath}", ex);
        }
    }

    public void ShowAllHidden() => Hider.ShowAll();

    /// <summary>Update the panic shortcut (null disables it) and re-register.</summary>
    public void SetPanicHotKey(HotKeyCombo? combo)
    {
        Config.PanicHotKey = combo;
        SaveAndReapply();
    }

    private static bool IsRunning(string processName)
    {
        var ps = Process.GetProcessesByName(processName);
        bool any = ps.Length > 0;
        foreach (var p in ps) p.Dispose();
        return any;
    }

    // ---- Floating icons ----
    public void ReconcileFloatingIcons()
    {
        var wanted = Config.Apps
            .Where(a => a.ShowFloatingIcon)
            .ToDictionary(a => a.Id);

        // Close windows that are no longer wanted.
        foreach (var id in _floating.Keys.ToList())
        {
            if (!wanted.ContainsKey(id))
            {
                _floating[id].ForceClose();
                _floating.Remove(id);
            }
        }

        // Open windows that are newly wanted; refresh existing ones.
        foreach (var (id, entry) in wanted)
        {
            if (_floating.TryGetValue(id, out var existing))
            {
                existing.UpdateEntry(entry);
            }
            else
            {
                var win = new FloatingIconWindow(entry, this);
                _floating[id] = win;
                win.Show();
            }
        }
    }

    public void PersistIconPosition(AppEntry entry, double x, double y)
    {
        entry.IconX = x;
        entry.IconY = y;
        Save();
    }

    public void DisableFloatingIcon(AppEntry entry)
    {
        entry.ShowFloatingIcon = false;
        SaveAndReapply();
    }

    public void Dispose()
    {
        foreach (var w in _floating.Values) w.ForceClose();
        _floating.Clear();
        HotKeys.Dispose();
        Hider.ShowAll(); // never leave a window invisible on exit
    }
}
