using System.IO;
using System.Windows;
using HideIt.Models;
using HideIt.Services;
using Microsoft.Win32;

namespace HideIt.Views;

/// <summary>Picks a running app (or browses to an .exe) and returns a new <see cref="AppEntry"/>.</summary>
public partial class AddAppDialog : Window
{
    private readonly ProcessCatalog _catalog;

    /// <summary>The created entry, set when the dialog returns true.</summary>
    public AppEntry? Result { get; private set; }

    public AddAppDialog(ProcessCatalog catalog)
    {
        InitializeComponent();
        _catalog = catalog;
        AppList.ItemsSource = _catalog.GetRunningAppsWithWindows().ToList();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (AppList.SelectedItem is RunningApp app)
        {
            Result = new AppEntry
            {
                ProcessName = app.ProcessName,
                DisplayName = app.DisplayName,
                ExePath = app.ExePath,
            };
            DialogResult = true;
            return;
        }
        MessageBox.Show(this, "Select an app from the list, or use \"Browse for .exe…\".",
            "Add an app", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AppList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (AppList.SelectedItem is RunningApp)
            Ok_Click(sender, e);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select an application",
            Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != true) return;

        var path = dlg.FileName;
        var name = Path.GetFileNameWithoutExtension(path);
        Result = new AppEntry
        {
            ProcessName = name,
            DisplayName = name,
            ExePath = path,
        };
        DialogResult = true;
    }
}
