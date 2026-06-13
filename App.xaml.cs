using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using HideIt.Services;
using HideIt.Views;
using WpfControls = System.Windows.Controls;

namespace HideIt;

public partial class App : Application
{
    private const string MutexName = "HideIt.SingleInstance.Mutex";

    private Mutex? _mutex;
    private AppController? _controller;
    private TaskbarIcon? _tray;
    private MainWindow? _mainWindow;
    private WpfControls.MenuItem? _startupItem;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance: if we don't own the mutex, another HideIt already runs the tray.
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
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

        _controller = new AppController();
        _controller.ToggleAppVisibilityRequested += ToggleTrayVisibility;
        _controller.Load();

        BuildTray();
        MaybeShowOnboarding();
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

        menu.Items.Add(new WpfControls.Separator());

        var updates = new WpfControls.MenuItem { Header = "Check for updates" };
        updates.Click += (_, _) => OpenUrl(AppInfo.ReleasesUrl);
        menu.Items.Add(updates);

        var exit = new WpfControls.MenuItem { Header = "Exit" };
        exit.Click += (_, _) => ExitApp();
        menu.Items.Add(exit);

        _tray.ContextMenu = menu;
        _tray.TrayMouseDoubleClick += (_, _) => ShowSettings();
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
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();

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
        try { _mutex?.ReleaseMutex(); } catch { /* not owned */ }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
