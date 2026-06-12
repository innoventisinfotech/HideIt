using System.Windows;
using HideIt.ViewModels;

namespace HideIt.Views;

public partial class MainWindow : Window
{
    public MainWindow(AppController controller)
    {
        InitializeComponent();
        DataContext = new MainViewModel(controller);
    }
}
