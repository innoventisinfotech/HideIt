using System.Windows.Interop;
using HideIt.Models;

namespace HideIt.Services;

/// <summary>
/// Registers global hotkeys against a dedicated message-only window so they live
/// independently of any visible window (the settings window can be closed to tray).
/// </summary>
public sealed class HotKeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, HotKeyCombo> _idToCombo = new();
    private int _nextId = 1;

    /// <summary>Raised on the UI thread when a registered combo is pressed.</summary>
    public event Action<HotKeyCombo>? Pressed;

    /// <summary>Raised when a combo could not be registered (already owned globally).</summary>
    public event Action<HotKeyCombo>? RegistrationFailed;

    public HotKeyService()
    {
        var p = new HwndSourceParameters("HideIt.HotKeyWindow")
        {
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE — message-only window
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>Re-register the given combos, deduplicated by value equality.</summary>
    public void RegisterAll(IEnumerable<HotKeyCombo> combos)
    {
        UnregisterAll();
        foreach (var combo in combos.Distinct())
        {
            uint mods = ToWin32Mods(combo.Modifiers) | Native.MOD_NOREPEAT;
            uint vk = (uint)combo.VirtualKey;
            int id = _nextId++;
            if (Native.RegisterHotKey(_source.Handle, id, mods, vk))
                _idToCombo[id] = combo;
            else
                RegistrationFailed?.Invoke(combo);
        }
    }

    public void UnregisterAll()
    {
        foreach (var id in _idToCombo.Keys)
            Native.UnregisterHotKey(_source.Handle, id);
        _idToCombo.Clear();
        _nextId = 1;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _idToCombo.TryGetValue(wParam.ToInt32(), out var combo))
        {
            Pressed?.Invoke(combo);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static uint ToWin32Mods(Mod m)
    {
        uint r = 0;
        if (m.HasFlag(Mod.Alt)) r |= Native.MOD_ALT;
        if (m.HasFlag(Mod.Ctrl)) r |= Native.MOD_CONTROL;
        if (m.HasFlag(Mod.Shift)) r |= Native.MOD_SHIFT;
        if (m.HasFlag(Mod.Win)) r |= Native.MOD_WIN;
        return r;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
