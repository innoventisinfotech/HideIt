namespace HideIt.Services;

/// <summary>A window we hid: its handle, the extended style to restore, and its process id.</summary>
public sealed record HiddenWin(IntPtr Hwnd, long OriginalExStyle, uint Pid);

/// <summary>
/// Hides/shows all top-level windows of a process. Show() works off stored handles
/// (not enumeration), because hidden windows no longer pass the "visible" filter.
/// When <see cref="MuteWhileHidden"/> is on, a hidden window's process audio is muted
/// and unmuted on restore.
/// </summary>
public sealed class WindowHider
{
    private readonly Dictionary<string, List<HiddenWin>> _hidden =
        new(StringComparer.OrdinalIgnoreCase);

    // Individually-hidden windows (the "hide one specific window" feature), newest last.
    private readonly List<HiddenWin> _individual = new();

    private readonly AudioService _audio = new();

    /// <summary>Mute a hidden window's process audio while it's hidden.</summary>
    public bool MuteWhileHidden { get; private set; }

    public bool IsHidden(string processName) =>
        _hidden.TryGetValue(processName, out var list) && list.Count > 0;

    public bool HasIndividuallyHidden => _individual.Count > 0;

    public bool IsWindowHidden(IntPtr hwnd) => _individual.Any(h => h.Hwnd == hwnd);

    /// <summary>Turn muting on/off, applying it immediately to currently-hidden windows.</summary>
    public void SetMuteWhileHidden(bool on)
    {
        if (MuteWhileHidden == on) return;
        MuteWhileHidden = on;

        if (on)
        {
            foreach (var w in AllHidden()) _audio.Mute(w.Pid);
        }
        else
        {
            _audio.UnmuteAll();
        }
    }

    private IEnumerable<HiddenWin> AllHidden() =>
        _hidden.Values.SelectMany(list => list).Concat(_individual);

    /// <summary>Restore one specific individually-hidden window and focus it.</summary>
    public void ShowSpecificWindow(IntPtr hwnd)
    {
        int idx = _individual.FindIndex(h => h.Hwnd == hwnd);
        if (idx < 0) return;
        var h = _individual[idx];
        _individual.RemoveAt(idx);
        Restore(h);
        Native.SetForegroundWindow(h.Hwnd);
    }

    /// <summary>Hide one specific window by handle (used by the window picker).</summary>
    public void HideWindow(IntPtr hwnd)
    {
        if (_individual.Any(h => h.Hwnd == hwnd)) return;
        _individual.Add(HideOne(hwnd));
    }

    /// <summary>Restore the most recently individually-hidden window and focus it.</summary>
    public void ShowLastWindow()
    {
        if (_individual.Count == 0) return;
        var h = _individual[^1];
        _individual.RemoveAt(_individual.Count - 1);
        Restore(h);
        Native.SetForegroundWindow(h.Hwnd);
    }

    public void Hide(string processName)
    {
        if (IsHidden(processName)) return;

        var list = new List<HiddenWin>();
        foreach (var hwnd in Native.GetTopLevelWindows(processName))
            list.Add(HideOne(hwnd));

        if (list.Count > 0)
            _hidden[processName] = list;
    }

    public void Show(string processName)
    {
        if (!_hidden.TryGetValue(processName, out var list)) return;

        foreach (var w in list)
            Restore(w);

        if (list.Count > 0)
            Native.SetForegroundWindow(list[0].Hwnd);

        _hidden.Remove(processName);
    }

    public void Toggle(string processName)
    {
        if (IsHidden(processName)) Show(processName);
        else Hide(processName);
    }

    /// <summary>Restore everything — used for the panic hotkey, tray command and on exit.</summary>
    public void ShowAll()
    {
        foreach (var key in _hidden.Keys.ToList())
            Show(key);

        foreach (var w in _individual)
            Restore(w);
        _individual.Clear();

        _audio.UnmuteAll(); // belt-and-suspenders: never leave anything muted
    }

    // ---- shared hide/restore primitives ----
    private HiddenWin HideOne(IntPtr hwnd)
    {
        long ex = Native.GetExStyle(hwnd);
        Native.SetExStyle(hwnd, (ex | Native.WS_EX_TOOLWINDOW) & ~Native.WS_EX_APPWINDOW);
        Native.ShowWindow(hwnd, Native.SW_HIDE);

        uint pid = Native.GetWindowPid(hwnd);
        if (MuteWhileHidden) _audio.Mute(pid);
        return new HiddenWin(hwnd, ex, pid);
    }

    private void Restore(HiddenWin w)
    {
        Native.SetExStyle(w.Hwnd, w.OriginalExStyle); // restore the exact original style
        Native.ShowWindow(w.Hwnd, Native.SW_SHOW);
        _audio.Unmute(w.Pid); // no-op if we didn't mute it
    }
}
