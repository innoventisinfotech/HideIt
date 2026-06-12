using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using HideIt.Models;

namespace HideIt.ViewModels;

/// <summary>Row view-model wrapping an <see cref="AppEntry"/> for the settings grid.</summary>
public partial class AppEntryVm : ObservableObject
{
    private readonly Action _onChanged;

    public AppEntry Model { get; }

    public AppEntryVm(AppEntry model, ImageSource? icon, Action onChanged)
    {
        Model = model;
        Icon = icon;
        _onChanged = onChanged;
        _showFloatingIcon = model.ShowFloatingIcon;
    }

    public ImageSource? Icon { get; }

    public string DisplayName => Model.DisplayName;

    public string ProcessName => Model.ProcessName;

    public string ShortcutText => Model.HotKey?.Display() ?? "(none)";

    [ObservableProperty]
    private bool _showFloatingIcon;

    partial void OnShowFloatingIconChanged(bool value)
    {
        Model.ShowFloatingIcon = value;
        _onChanged();
    }

    /// <summary>Call after the model's hotkey changes so the grid text refreshes.</summary>
    public void RefreshHotKey() => OnPropertyChanged(nameof(ShortcutText));
}
