using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace HideIt.Views;

/// <summary>Lists open windows so the user can hide specific ones (not the whole process).</summary>
public partial class HideWindowDialog : Window
{
    /// <summary>Row wrapper that carries the checkbox state for one open window.</summary>
    public sealed class Row
    {
        public required OpenWindow Win { get; init; }
        public bool IsChecked { get; set; }
        public string Title => Win.Title;
        public string ProcessName => Win.ProcessName;
        public ImageSource? Icon => Win.Icon;
    }

    private readonly AppController _controller;

    /// <summary>Handles of the windows the user chose to hide.</summary>
    public List<IntPtr> Result { get; private set; } = new();

    public HideWindowDialog(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        Reload();
    }

    private void Reload() =>
        List.ItemsSource = _controller.GetOpenWindows().Select(w => new Row { Win = w }).ToList();

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private List<IntPtr> CheckedHandles() =>
        (List.ItemsSource as IEnumerable<Row>)!
            .Where(r => r.IsChecked)
            .Select(r => r.Win.Hwnd)
            .ToList();

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        Result = CheckedHandles();
        if (Result.Count == 0)
        {
            MessageBox.Show(this, "Tick at least one window to hide.",
                "Hide a window", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private void Assign_Click(object sender, RoutedEventArgs e)
    {
        var handles = CheckedHandles();
        if (handles.Count == 0)
        {
            MessageBox.Show(this, "Tick the window(s) you want the shortcut to toggle.",
                "Assign shortcut", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var capture = new HotKeyCaptureDialog { Owner = this };
        if (capture.ShowDialog() != true || capture.Result == null)
            return; // cancelled or cleared

        bool ok = _controller.AddTempWindowBinding(capture.Result, handles);
        if (ok)
        {
            MessageBox.Show(this,
                $"{capture.Result.Display()} will now hide/show the selected window(s).\n\n" +
                "This is temporary — it lasts until those windows close or HideIt restarts.",
                "Shortcut assigned", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = false; // closes the dialog; nothing for the caller to hide
        }
        else
        {
            MessageBox.Show(this,
                $"{capture.Result.Display()} is already in use by another app and couldn't be registered. Try a different combination.",
                "Shortcut in use", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
