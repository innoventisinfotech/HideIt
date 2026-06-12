namespace HideIt.Services;

/// <summary>A window we hid, plus the extended style it had so we can restore it exactly.</summary>
public sealed record HiddenWin(IntPtr Hwnd, long OriginalExStyle);

/// <summary>
/// Hides/shows all top-level windows of a process. Show() works off stored handles
/// (not enumeration), because hidden windows no longer pass the "visible" filter.
/// </summary>
public sealed class WindowHider
{
    private readonly Dictionary<string, List<HiddenWin>> _hidden =
        new(StringComparer.OrdinalIgnoreCase);

    public bool IsHidden(string processName) =>
        _hidden.TryGetValue(processName, out var list) && list.Count > 0;

    public void Hide(string processName)
    {
        if (IsHidden(processName)) return;

        var list = new List<HiddenWin>();
        foreach (var hwnd in Native.GetTopLevelWindows(processName))
        {
            long ex = Native.GetExStyle(hwnd);
            Native.SetExStyle(hwnd, (ex | Native.WS_EX_TOOLWINDOW) & ~Native.WS_EX_APPWINDOW);
            Native.ShowWindow(hwnd, Native.SW_HIDE);
            list.Add(new HiddenWin(hwnd, ex));
        }

        if (list.Count > 0)
            _hidden[processName] = list;
    }

    public void Show(string processName)
    {
        if (!_hidden.TryGetValue(processName, out var list)) return;

        foreach (var (hwnd, originalEx) in list)
        {
            Native.SetExStyle(hwnd, originalEx); // restore the exact original style
            Native.ShowWindow(hwnd, Native.SW_SHOW);
        }

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
    }
}
