using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace OneSchedule;

internal sealed class TrayService : IDisposable
{
    private readonly Window _window;
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ContextMenuStrip _menu;
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
        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add("打开", null, (_, _) => Restore());
        _menu.Items.Add("退出", null, (_, _) => _exit());
        _icon = new Forms.NotifyIcon { Icon = _trayIcon, Text = "OneSchedule", ContextMenuStrip = _menu, Visible = true };
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
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
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
