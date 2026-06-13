using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace HideIt.Services;

/// <summary>All Win32 P/Invoke, constants and the window-enumeration helpers.</summary>
internal static class Native
{
    // ---- Constants ----
    public const int GWL_EXSTYLE = -20;

    public const long WS_EX_TOOLWINDOW = 0x00000080; // removes from taskbar AND Alt+Tab
    public const long WS_EX_APPWINDOW = 0x00040000;  // forces onto taskbar
    public const long WS_EX_NOACTIVATE = 0x08000000; // floating icon: never steal focus

    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;
    public const int SW_SHOWNA = 8;

    public const uint GW_OWNER = 4;

    public const uint MOD_ALT = 0x1;
    public const uint MOD_CONTROL = 0x2;
    public const uint MOD_SHIFT = 0x4;
    public const uint MOD_WIN = 0x8;
    public const uint MOD_NOREPEAT = 0x4000;

    public const int DWMWA_CLOAKED = 14;

    public const int WM_HOTKEY = 0x0312;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // ---- Hotkeys ----
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ---- Window show/hide + focus ----
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    // ---- Enumeration ----
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    // ---- Extended styles: Ptr variants on 64-bit, plain on 32-bit ----
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    public static long GetExStyle(IntPtr hWnd) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, GWL_EXSTYLE).ToInt64()
                         : GetWindowLong32(hWnd, GWL_EXSTYLE);

    public static void SetExStyle(IntPtr hWnd, long exStyle)
    {
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(hWnd, GWL_EXSTYLE, new IntPtr(exStyle));
        else
            SetWindowLong32(hWnd, GWL_EXSTYLE, (int)exStyle);
    }

    /// <summary>Top-level "real" (Alt+Tab-able) windows belonging to the named process.</summary>
    public static List<IntPtr> GetTopLevelWindows(string processName)
    {
        var pids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName(processName))
        {
            try { pids.Add((uint)p.Id); }
            catch { /* process exited */ }
            finally { p.Dispose(); }
        }

        var result = new List<IntPtr>();
        if (pids.Count == 0) return result;

        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pids.Contains(pid) && IsRealAppWindow(hWnd))
                result.Add(hWnd);
            return true;
        }, IntPtr.Zero);

        return result;
    }

    public static uint GetWindowPid(IntPtr hWnd)
    {
        GetWindowThreadProcessId(hWnd, out uint pid);
        return pid;
    }

    public static string GetWindowTitle(IntPtr hWnd)
    {
        int len = GetWindowTextLength(hWnd);
        if (len == 0) return "";
        var sb = new StringBuilder(len + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>A single real top-level window: its handle, title and owning process id.</summary>
    public readonly record struct WindowInfo(IntPtr Hwnd, string Title, uint Pid);

    /// <summary>Every "real" (Alt+Tab-able) top-level window across all processes.</summary>
    public static List<WindowInfo> GetAllRealWindows()
    {
        var result = new List<WindowInfo>();
        EnumWindows((hWnd, _) =>
        {
            if (IsRealAppWindow(hWnd))
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                result.Add(new WindowInfo(hWnd, GetWindowTitle(hWnd), pid));
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>True for a visible, owner-less, titled, non-toolwindow, non-cloaked top-level window.</summary>
    public static bool IsRealAppWindow(IntPtr hWnd)
    {
        if (!IsWindowVisible(hWnd)) return false;
        if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return false;
        if (GetWindowTextLength(hWnd) == 0) return false;
        if ((GetExStyle(hWnd) & WS_EX_TOOLWINDOW) != 0) return false;
        if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;
        return true;
    }
}
