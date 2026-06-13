using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using HideIt.Services;
using HideIt.Views;
using WpfControls = System.Windows.Controls;

namespace HideIt;

public partial class App : Application
{
    private const string MutexName = "HideIt.SingleInstance.Mutex";
    private const string ShowEventName = "HideIt.ShowSettings.Event";

    /// <summary>Passed in the startup Run-key so a login launch starts silently in the tray.</summary>
    public const string StartupArg = "--startup";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private AppController? _controller;
    private TaskbarIcon? _tray;
    private MainWindow? _mainWindow;
    private WpfControls.MenuItem? _startupItem;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance: if we don't own the mutex, tell the running copy to show its
        // window (so double-clicking the exe again pops Settings), then exit.
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
            {
                existing.Set();
                existing.Dispose();
            }
            Shutdown();
            return;
        }

        Logger.Init();
        DispatcherUnhandledException += (_, args) =>
        {
            Logger.LogException("DispatcherUnhandledException", args.Exception);
            // Keep running where possible; the tray app should survive a stray UI exception.
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.LogException("UnhandledException", args.ExceptionObject as Exception);

        StartShowSettingsListener();

        _controller = new AppController();
        _controller.ToggleAppVisibilityRequested += ToggleTrayVisibility;
        _controller.Load();

        BuildTray();

        bool startedAtLogin = e.Args.Any(a => string.Equals(a, StartupArg, StringComparison.OrdinalIgnoreCase));

        if (!_controller.Config.FirstRunComplete)
        {
            MaybeShowOnboarding();
        }
        else if (!startedAtLogin)
        {
            // A manual launch should open the window — defer until the message loop is
            // pumping so the window reliably renders (showing during OnStartup can fail).
            Dispatcher.BeginInvoke(new Action(ShowSettings),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>Background listener: a second instance signals this to open Settings.</summary>
    private void StartShowSettingsListener()
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) => Dispatcher.BeginInvoke(new Action(ShowTray)),
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    private void MaybeShowOnboarding()
    {
        if (_controller!.Config.FirstRunComplete) return;

        var onboarding = new OnboardingWindow();
        onboarding.ShowDialog();

        _controller.Config.FirstRunComplete = true;
        _controller.Save();

        if (onboarding.OpenSettingsRequested)
            ShowSettings();
    }

    private void BuildTray()
    {
        _tray = new TaskbarIcon { ToolTipText = "HideIt" };
        try
        {
            _tray.IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/app.ico"));
        }
        catch (Exception ex)
        {
            Logger.LogException("Tray icon load", ex);
        }

        var menu = new WpfControls.ContextMenu();

        var settings = new WpfControls.MenuItem { Header = "Settings" };
        settings.Click += (_, _) => ShowSettings();
        menu.Items.Add(settings);

        var hideWindow = new WpfControls.MenuItem { Header = "Hide a window…" };
        hideWindow.Click += (_, _) => OpenHideWindowPicker();
        menu.Items.Add(hideWindow);

        var restore = new WpfControls.MenuItem { Header = "Restore all hidden" };
        restore.Click += (_, _) => _controller!.ShowAllHidden();
        menu.Items.Add(restore);

        var hideSelf = new WpfControls.MenuItem { Header = "Hide HideIt icon" };
        hideSelf.Click += (_, _) => HideTrayFromMenu();
        menu.Items.Add(hideSelf);

        _startupItem = new WpfControls.MenuItem
        {
            Header = "Run at Windows startup",
            IsCheckable = true,
            IsChecked = _controller!.Startup.IsEnabled(),
        };
        _startupItem.Click += (_, _) =>
        {
            bool on = _startupItem!.IsChecked;
            _controller!.Startup.SetEnabled(on);
            _controller.Config.RunAtStartup = on;
            _controller.Save();
        };
        menu.Items.Add(_startupItem);

        var shortcut = new WpfControls.MenuItem { Header = "Create shortcut" };
        var scDesktop = new WpfControls.MenuItem { Header = "On Desktop" };
        scDesktop.Click += (_, _) => MakeShortcut(desktop: true);
        var scStart = new WpfControls.MenuItem { Header = "In Start Menu" };
        scStart.Click += (_, _) => MakeShortcut(desktop: false);
        shortcut.Items.Add(scDesktop);
        shortcut.Items.Add(scStart);
        menu.Items.Add(shortcut);

        menu.Items.Add(new WpfControls.Separator());

        var updates = new WpfControls.MenuItem { Header = "Check for updates" };
        updates.Click += (_, _) => OpenUrl(AppInfo.ReleasesUrl);
        menu.Items.Add(updates);

        var exit = new WpfControls.MenuItem { Header = "Exit" };
        exit.Click += (_, _) => ExitApp();
        menu.Items.Add(exit);

        _tray.ContextMenu = menu;
        // Open Settings on single OR double left-click (double-click event can be flaky).
        var showCommand = new RelayCommand(ShowSettings);
        _tray.LeftClickCommand = showCommand;
        _tray.DoubleClickCommand = showCommand;
        _tray.ForceCreate(enablesEfficiencyMode: false);
    }

    private void ShowSettings()
    {
        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow(_controller!);
            _mainWindow.Closing += (_, args) =>
            {
                // Closing hides to tray; only a real Exit shuts the app down.
                if (!_exiting)
                {
                    args.Cancel = true;
                    _mainWindow!.Hide();
                }
            };
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
        // Reliably bring it to the foreground from a background tray process.
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _mainWindow.Focus();

        if (_startupItem != null)
            _startupItem.IsChecked = _controller!.Startup.IsEnabled();
    }

    private void OpenHideWindowPicker()
    {
        var dlg = new HideWindowDialog(_controller!);
        if (_mainWindow is { IsVisible: true })
            dlg.Owner = _mainWindow;
        if (dlg.ShowDialog() == true)
            _controller!.HideSpecificWindows(dlg.Result);
    }

    /// <summary>Toggle HideIt's own tray icon. Invoked by the global show/hide shortcut.</summary>
    private void ToggleTrayVisibility()
    {
        if (_tray == null) return;
        if (_tray.Visibility == Visibility.Visible)
            HideTray();
        else
            ShowTray();
    }

    private void HideTray()
    {
        if (_tray == null) return;
        _tray.Visibility = Visibility.Hidden;   // removes the icon from the tray + overflow
        _mainWindow?.Hide();                     // also tuck away the settings window
    }

    private void ShowTray()
    {
        if (_tray == null) return;
        _tray.Visibility = Visibility.Visible;
        ShowSettings();                          // clear feedback that HideIt is back
    }

    private void HideTrayFromMenu()
    {
        // Refuse to hide if there's no working shortcut to bring HideIt back — otherwise
        // the user could only recover via Task Manager.
        if (!_controller!.AppToggleHotKeyWorks)
        {
            MessageBox.Show(
                "Set a working \"Show / hide HideIt\" shortcut in Settings first.\n\n" +
                "Without one, you couldn't bring HideIt back after hiding its icon " +
                "(you'd have to end it from Task Manager).",
                "HideIt", MessageBoxButton.OK, MessageBoxImage.Warning);
            ShowSettings();
            return;
        }
        HideTray();
    }

    private void MakeShortcut(bool desktop)
    {
        bool ok = desktop
            ? ShortcutService.CreateDesktopShortcut()
            : ShortcutService.CreateStartMenuShortcut();
        string where = desktop ? "Desktop" : "Start Menu";
        MessageBox.Show(
            ok ? $"HideIt shortcut added to your {where}."
               : $"Couldn't create the {where} shortcut. See %AppData%\\HideIt\\logs.",
            "HideIt", MessageBoxButton.OK,
            ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.LogException($"OpenUrl {url}", ex);
        }
    }

    private void ExitApp()
    {
        _exiting = true;
        _controller?.Dispose(); // restores all hidden windows + unregisters hotkeys
        _tray?.Dispose();
        _mainWindow?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_exiting)
        {
            _controller?.Dispose();
            _tray?.Dispose();
        }
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { /* not owned */ }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
