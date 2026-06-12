using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using HideIt.Models;

namespace HideIt.ViewModels;

/// <summary>Row view-model wrapping an <see cref="AppEntry"/> for the settings grid.</summary>
public partial class AppEntryVm : ObservableObject
{
    public AppEntry Model { get; }

    public AppEntryVm(AppEntry model, ImageSource? icon)
    {
        Model = model;
        Icon = icon;
    }

    public ImageSource? Icon { get; }

    public string DisplayName => Model.DisplayName;

    public string ProcessName => Model.ProcessName;

    public string ShortcutText => Model.HotKey?.Display() ?? "(none)";

    /// <summary>Call after the model's hotkey changes so the grid text refreshes.</summary>
    public void RefreshHotKey() => OnPropertyChanged(nameof(ShortcutText));
}
