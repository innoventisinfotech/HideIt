namespace HideIt.Models;

/// <summary>One configured app: how to match its process, its shortcut and icon prefs.</summary>
public sealed class AppEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Process name without ".exe" (e.g. "chrome"); matched case-insensitively.</summary>
    public string ProcessName { get; set; } = "";

    public string DisplayName { get; set; } = "";

    /// <summary>Full path to the executable, used for the icon (and possibly launching later).</summary>
    public string? ExePath { get; set; }

    /// <summary>Assigned shortcut, or null when unassigned.</summary>
    public HotKeyCombo? HotKey { get; set; }

    public bool ShowFloatingIcon { get; set; }

    /// <summary>Last floating-icon position, in WPF device-independent units.</summary>
    public double IconX { get; set; }
    public double IconY { get; set; }
}
