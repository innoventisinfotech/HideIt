namespace HideIt.Models;

/// <summary>Root configuration object, serialized to %AppData%\HideIt\config.json.</summary>
public sealed class AppConfig
{
    public List<AppEntry> Apps { get; set; } = new();

    public bool RunAtStartup { get; set; }

    /// <summary>
    /// Restores every hidden window. Defaults to Ctrl+Alt+` (VK_OEM_3 = 0xC0).
    /// Null disables the panic shortcut. Existing configs without this field keep the default.
    /// </summary>
    public HotKeyCombo? PanicHotKey { get; set; } = new(Mod.Ctrl | Mod.Alt, 0xC0);

    /// <summary>
    /// Shows/hides HideIt's own tray icon. Defaults to Ctrl+Alt+Shift+H (VK 0x48).
    /// Null disables it. Needed to bring HideIt back once its tray icon is hidden.
    /// </summary>
    public HotKeyCombo? AppToggleHotKey { get; set; } = new(Mod.Ctrl | Mod.Alt | Mod.Shift, 0x48);

    /// <summary>
    /// Restores the most recently individually-hidden window (from the window picker).
    /// Defaults to Ctrl+Alt+Shift+S (VK 0x53). Null disables it.
    /// </summary>
    public HotKeyCombo? ShowLastHotKey { get; set; } = new(Mod.Ctrl | Mod.Alt | Mod.Shift, 0x53);

    /// <summary>False until the one-time onboarding panel has been shown.</summary>
    public bool FirstRunComplete { get; set; }
}
