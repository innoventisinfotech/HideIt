using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HideIt.Models;
using HideIt.Views;

namespace HideIt.ViewModels;

/// <summary>Drives the settings window: the app list, add/remove, shortcuts, startup.</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly AppController _controller;

    public ObservableCollection<AppEntryVm> Apps { get; } = new();

    [ObservableProperty]
    private bool _runAtStartup;

    [ObservableProperty]
    private string? _statusMessage;

    public MainViewModel(AppController controller)
    {
        _controller = controller;
        _runAtStartup = controller.Startup.IsEnabled();
        controller.HotKeyRegistrationFailed += OnHotKeyFailed;
        controller.ConfigChanged += OnConfigChanged;
        ReloadApps();
    }

    private void OnConfigChanged() =>
        Application.Current?.Dispatcher.BeginInvoke(new Action(ReloadApps));

    private void OnHotKeyFailed(HotKeyCombo combo) =>
        Application.Current?.Dispatcher.BeginInvoke(() =>
            StatusMessage = $"Shortcut {combo.Display()} is already in use by another app and was not registered.");

    private void ReloadApps()
    {
        Apps.Clear();
        foreach (var entry in _controller.Config.Apps)
        {
            var icon = _controller.Catalog.GetIconFor(entry.ExePath);
            Apps.Add(new AppEntryVm(entry, icon));
        }
    }

    partial void OnRunAtStartupChanged(bool value)
    {
        _controller.Startup.SetEnabled(value);
        _controller.Config.RunAtStartup = value;
        _controller.Save();
    }

    [RelayCommand]
    private void AddApp()
    {
        var dlg = new AddAppDialog(_controller.Catalog) { Owner = OwnerWindow() };
        if (dlg.ShowDialog() == true && dlg.Result != null)
        {
            _controller.Config.Apps.Add(dlg.Result);
            _controller.SaveAndReapply();
        }
    }

    [RelayCommand]
    private void Remove(AppEntryVm? vm)
    {
        if (vm == null) return;
        _controller.Config.Apps.Remove(vm.Model);
        _controller.SaveAndReapply();
    }

    [RelayCommand]
    private void SetHotKey(AppEntryVm? vm)
    {
        if (vm == null) return;
        var dlg = new HotKeyCaptureDialog { Owner = OwnerWindow() };
        if (dlg.ShowDialog() == true)
        {
            vm.Model.HotKey = dlg.Result; // null clears it
            vm.RefreshHotKey();
            StatusMessage = null;
            _controller.SaveAndReapply();
        }
    }

    [RelayCommand]
    private void RestoreAll() => _controller.ShowAllHidden();

    // ---- Panic shortcut ----
    public string PanicHotKeyText => _controller.Config.PanicHotKey?.Display() ?? "(none)";

    [RelayCommand]
    private void SetPanicHotKey()
    {
        var dlg = new HotKeyCaptureDialog { Owner = OwnerWindow() };
        if (dlg.ShowDialog() == true)
        {
            _controller.SetPanicHotKey(dlg.Result); // null clears it
            OnPropertyChanged(nameof(PanicHotKeyText));
            StatusMessage = null;
        }
    }

    // ---- Show/hide HideIt itself ----
    public string AppToggleHotKeyText => _controller.Config.AppToggleHotKey?.Display() ?? "(none)";

    [RelayCommand]
    private void SetAppToggleHotKey()
    {
        var dlg = new HotKeyCaptureDialog { Owner = OwnerWindow() };
        if (dlg.ShowDialog() == true)
        {
            _controller.SetAppToggleHotKey(dlg.Result); // null clears it
            OnPropertyChanged(nameof(AppToggleHotKeyText));
            StatusMessage = null;
        }
    }

    private static Window? OwnerWindow() =>
        Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
}
