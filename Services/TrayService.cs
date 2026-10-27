using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace OneSchedule;

internal sealed class TrayService : IDisposable
{
    private readonly Window _window;
    private readonly Forms.NotifyIcon _icon;
    private readonly ContextMenu _menu;
    private readonly Icon _trayIcon;
    private readonly Action _exit;
    private Window[] _hiddenOwnedWindows = [];
    private bool _disposed;

    public TrayService(Window window, Action exit)
    {
        _window = window;
        _exit = exit;
        using var resource = Application.GetResourceStream(new Uri("/OneSchedule;component/icon/whale.ico", UriKind.Relative)).Stream;
        using var sourceIcon = new Icon(resource);
        _trayIcon = (Icon)sourceIcon.Clone();
        _menu = new ContextMenu { Style = (Style)Application.Current.FindResource("TrayContextMenuStyle"), Placement = PlacementMode.MousePoint };
        var open = new MenuItem { Header = "打开" };
        var exitItem = new MenuItem { Header = "退出" };
        open.Click += (_, _) => { _menu.IsOpen = false; Restore(); };
        exitItem.Click += (_, _) => { _menu.IsOpen = false; _exit(); };
        _menu.Items.Add(open);
        _menu.Items.Add(exitItem);
        _menu.Opened += Menu_Opened;
        _icon = new Forms.NotifyIcon { Icon = _trayIcon, Text = "OneSchedule", Visible = true };
        _icon.MouseUp += OnMouseUp;
        _icon.DoubleClick += OnDoubleClick;
        _window.Closing += OnClosing;
        _window.StateChanged += OnStateChanged;
    }

    public void Hide()
    {
        if (_disposed) return;
        if (_window.IsVisible)
            _hiddenOwnedWindows = VisibleOwnedWindows(_window).ToArray();
        _window.ShowInTaskbar = false;
        _window.Hide();
    }

    public void Restore()
    {
        if (_disposed) return;
        _window.ShowInTaskbar = true;
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Show();
        // Changing taskbar visibility can leave the native window minimized even after WPF reports Normal.
        var mainHandle = new WindowInteropHelper(_window).Handle;
        if (IsIconic(mainHandle)) ShowWindow(mainHandle, 9);
        // WPF hides owned windows with their owner; restore the same dialog chain.
        foreach (var owned in _hiddenOwnedWindows)
            if (!owned.IsVisible && new WindowInteropHelper(owned).Handle != IntPtr.Zero) owned.Show();
        _hiddenOwnedWindows = [];
        var target = _window;
        while (target.OwnedWindows.Cast<Window>().LastOrDefault(owned => owned.IsVisible) is { } dialog)
            target = dialog;
        if (target.WindowState == WindowState.Minimized) target.WindowState = WindowState.Normal;
        target.Activate();
        SetForegroundWindow(new WindowInteropHelper(target).Handle);
    }

    private static IEnumerable<Window> VisibleOwnedWindows(Window owner)
    {
        foreach (Window child in owner.OwnedWindows)
        {
            if (!child.IsVisible) continue;
            yield return child;
            foreach (var nested in VisibleOwnedWindows(child)) yield return nested;
        }
    }

    private void OnMouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Right) ShowMenu();
    }

    internal void ShowMenu()
    {
        if (_disposed) return;
        _menu.IsOpen = true;
    }

    private void Menu_Opened(object sender, RoutedEventArgs e)
    {
        // Foreground the popup itself so outside clicks dismiss it without showing the hidden main window.
        if (PresentationSource.FromVisual(_menu) is HwndSource source) SetForegroundWindow(source.Handle);
        _menu.Focus();
    }

    private void OnDoubleClick(object? sender, EventArgs e) => Restore();
    private void OnClosing(object? sender, CancelEventArgs e) { e.Cancel = true; Hide(); }
    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_window.WindowState == WindowState.Minimized) Hide();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.Closing -= OnClosing;
        _window.StateChanged -= OnStateChanged;
        _icon.DoubleClick -= OnDoubleClick;
        _icon.MouseUp -= OnMouseUp;
        _menu.Opened -= Menu_Opened;
        _menu.IsOpen = false;
        _icon.Visible = false;
        _icon.Dispose();
        _trayIcon.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
