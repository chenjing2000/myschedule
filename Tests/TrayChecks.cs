using System.Windows;
using OneSchedule;

internal static class TrayChecks
{
    public static void Run()
    {
        var window = new Window { Width = 240, Height = 140, Opacity = 0, ShowActivated = false };
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        using (var tray = new TrayService(window, () => { }))
        {
            window.Show();
            window.Close();
            Require(!closed && !window.IsVisible && !window.ShowInTaskbar, "close hides without destroying window");
            tray.Restore();
            Require(window.IsVisible && window.ShowInTaskbar, "restore shows window and taskbar");
            window.WindowState = WindowState.Minimized;
            Require(!window.IsVisible && !window.ShowInTaskbar, "minimize hides quietly");
            tray.Restore();
            Require(window.IsVisible && window.WindowState == WindowState.Normal, "restore normalizes minimized window");
            var dialog = new Window { Owner = window, Width = 180, Height = 100, Opacity = 0, ShowActivated = false };
            dialog.Show();
            var editor = new Window { Owner = dialog, Width = 140, Height = 80, Opacity = 0, ShowActivated = false };
            try
            {
                editor.Show();
                tray.Hide();
                tray.Restore();
                Require(window.IsVisible && dialog.IsVisible && editor.IsVisible, "restore preserves nested owned windows");
            }
            finally { editor.Close(); dialog.Close(); }
        }
        window.Close();
        Require(closed, "disposed tray detaches close interception");
        Console.WriteLine("PASS: tray close, minimize, restore and disposal");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }
}
