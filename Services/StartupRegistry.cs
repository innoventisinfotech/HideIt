using Microsoft.Win32;

namespace HideIt.Services;

/// <summary>Toggles "run at Windows startup" via the per-user HKCU Run key.</summary>
public sealed class StartupRegistry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HideIt";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) != null;
    }

    public void SetEnabled(bool on)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (key == null) return;

        if (on)
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
                // Pass --startup so a login launch starts silently in the tray (no window).
                key.SetValue(ValueName, $"\"{exe}\" {App.StartupArg}");
        }
        else if (key.GetValue(ValueName) != null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
