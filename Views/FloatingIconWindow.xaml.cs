using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using HideIt.Models;
using HideIt.Services;

namespace HideIt.Views;

/// <summary>
/// A small always-on-top button showing the app's icon. Left-click toggles that one
/// app; drag repositions and persists; right-click offers "Hide this button".
/// It never steals focus (WS_EX_NOACTIVATE) and stays out of Alt+Tab (WS_EX_TOOLWINDOW).
/// </summary>
public partial class FloatingIconWindow : Window
{
    private const double DragThreshold = 4.0;

    private AppEntry _entry;
    private readonly AppController _controller;

    private Point _dragOrigin;
    private bool _dragging;
    private bool _moved;
    private bool _forceClose;

    public FloatingIconWindow(AppEntry entry, AppController controller)
    {
        InitializeComponent();
        _entry = entry;
        _controller = controller;

        if (entry.IconX == 0 && entry.IconY == 0)
        {
            // First time: drop it near the top-right of the work area.
            Left = SystemParameters.WorkArea.Right - 80;
            Top = SystemParameters.WorkArea.Top + 80;
        }
        else
        {
            Left = entry.IconX;
            Top = entry.IconY;
        }

        IconImage.Source = controller.Catalog.GetIconFor(entry.ExePath);
        BuildContextMenu();
    }

    public void UpdateEntry(AppEntry entry)
    {
        _entry = entry;
        IconImage.Source = _controller.Catalog.GetIconFor(entry.ExePath);
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenu();
        var hide = new MenuItem { Header = "Hide this button" };
        hide.Click += (_, _) => _controller.DisableFloatingIcon(_entry);
        menu.Items.Add(hide);
        Root.ContextMenu = menu;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        long ex = Native.GetExStyle(handle);
        Native.SetExStyle(handle, ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragOrigin = PointToScreen(e.GetPosition(this));
        _moved = false;
        _dragging = true;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;

        var current = PointToScreen(e.GetPosition(this));
        double dx = current.X - _dragOrigin.X;
        double dy = current.Y - _dragOrigin.Y;

        if (!_moved && Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold)
            return;

        _moved = true;
        Left += dx;
        Top += dy;
        _dragOrigin = current;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();

        if (_moved)
            _controller.PersistIconPosition(_entry, Left, Top);
        else
            _controller.ToggleSingle(_entry);
    }

    /// <summary>Close even though the app uses OnExplicitShutdown elsewhere.</summary>
    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Only the controller should close these (via ForceClose); ignore stray closes.
        if (!_forceClose)
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }
}
