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

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        Result = (List.ItemsSource as IEnumerable<Row>)!
            .Where(r => r.IsChecked)
            .Select(r => r.Win.Hwnd)
            .ToList();

        if (Result.Count == 0)
        {
            MessageBox.Show(this, "Tick at least one window to hide.",
                "Hide a window", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }
}
