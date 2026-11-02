using System.IO;
using System.Text;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using OneSchedule;

internal static class SettingsChecks
{
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "OneSchedule-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "OneSchedule.settings.json");
            var registrations = new List<bool>();
            var settings = new SettingsService(path, registrations.Add);
            Require(settings.Load() && settings.StartWithWindows && File.Exists(path), "first load persists default on");
            Require(registrations.SequenceEqual(new[] { true }), "default on is applied through fake registration");
            Require(settings.SetStartWithWindows(false) && !settings.StartWithWindows, "toggle off succeeds");
            var reopened = new SettingsService(path, registrations.Add);
            Require(reopened.Load() && !reopened.StartWithWindows && !registrations[^1], "false persists and is applied on reopening");
            byte[] before = File.ReadAllBytes(path);
            var calls = new List<bool>();
            var failing = new SettingsService(path, enabled =>
            {
                calls.Add(enabled);
                if (enabled) throw new IOException("simulated registration failure");
            });
            Require(failing.Load(), "false can load before failing toggle");
            Require(!failing.SetStartWithWindows(true) && !failing.StartWithWindows && failing.LastError.Length > 0,
                "failed registration leaves checkbox off and exposes error");
            Require(before.SequenceEqual(File.ReadAllBytes(path)) && calls.SequenceEqual(new[] { false, true, false }),
                "failed registration restores exact settings bytes and previous registration");
            File.WriteAllText(path, "{ broken settings", Encoding.UTF8);
            byte[] damaged = File.ReadAllBytes(path);
            int applyCount = registrations.Count;
            var malformed = new SettingsService(path, registrations.Add);
            Require(!malformed.Load() && !malformed.SetStartWithWindows(true) && malformed.LastError.Length > 0,
                "malformed settings are rejected with a visible error");
            Require(damaged.SequenceEqual(File.ReadAllBytes(path)) && registrations.Count == applyCount,
                "malformed file is preserved without touching registration");
            File.WriteAllText(path, "{\"startWithWindows\":true}");
            var failedLoad = new SettingsService(path, _ => throw new IOException("startup unavailable"));
            Require(!failedLoad.Load() && !failedLoad.StartWithWindows && failedLoad.LastError.Length > 0,
                "failed initial registration does not falsely report enabled");
            foreach (string invalid in new[] { "{}", "{\"startWithWindows\":1}", "{\"startWithWindows\":true,\"extra\":1}",
                "{\"startWithWindows\":true,\"startWithWindows\":false}" })
            {
                File.WriteAllText(path, invalid);
                var rejected = new SettingsService(path, _ => throw new Exception("invalid settings must not apply"));
                Require(!rejected.Load() && File.ReadAllText(path) == invalid, "invalid shape is preserved");
            }
            File.WriteAllText(path, "{\"startWithWindows\":false}");
            var externallyChanged = new SettingsService(path, registrations.Add);
            Require(externallyChanged.Load(), "external edit fixture loads");
            File.WriteAllText(path, "broken externally");
            Require(!externallyChanged.SetStartWithWindows(true) && File.ReadAllText(path) == "broken externally",
                "external malformed edit is never overwritten by a toggle");
            ConcurrentWriteChecks(directory);
            UiChecks(directory);
            Console.WriteLine("PASS: settings persistence, isolated startup, and settings menu checks");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void ConcurrentWriteChecks(string directory)
    {
        string initialPath = Path.Combine(directory, "concurrent-initial.settings.json");
        const string externalInitial = "externally created settings";
        var initialCalls = new List<bool>();
        var initial = new SettingsService(initialPath, enabled =>
        {
            initialCalls.Add(enabled);
            if (enabled) File.WriteAllText(initialPath, externalInitial);
        });
        Require(!initial.Load() && !initial.StartWithWindows && initial.LastError.Length > 0 &&
            File.ReadAllText(initialPath) == externalInitial && initialCalls.SequenceEqual(new[] { true, false }),
            "first creation preserves a file created concurrently and rolls startup registration back");

        string rollbackPath = Path.Combine(directory, "concurrent-rollback.settings.json");
        File.WriteAllText(rollbackPath, "{\"startWithWindows\":false}");
        const string externalReplacement = "external edit during registration";
        var rollbackCalls = new List<bool>();
        var rollback = new SettingsService(rollbackPath, enabled =>
        {
            rollbackCalls.Add(enabled);
            if (enabled)
            {
                File.WriteAllText(rollbackPath, externalReplacement);
                throw new IOException("simulated registration failure after external edit");
            }
        });
        Require(rollback.Load() && !rollback.SetStartWithWindows(true) && !rollback.StartWithWindows &&
            File.ReadAllText(rollbackPath) == externalReplacement &&
            rollbackCalls.SequenceEqual(new[] { false, true, false }) &&
            rollback.LastError.Contains("simulated registration failure") && rollback.LastError.Contains("已保留外部内容"),
            "failed toggle preserves a concurrent external edit and visibly reports incomplete file rollback");
    }
    private static void UiChecks(string directory)
    {
        string path = Path.Combine(directory, "window.settings.json");
        bool rejectEnable = false;
        var settings = new SettingsService(path, enabled =>
        {
            if (rejectEnable && enabled) throw new IOException("simulated UI registration failure");
        });
        Require(settings.Load(), "settings menu default fixture loads");
        var menuType = typeof(MainWindow).Assembly.GetType("OneSchedule.SettingsMenu");
        Require(menuType is not null, "gear settings must be a menu instead of a window");
        var menu = (ContextMenu)Activator.CreateInstance(menuType!,
            BindingFlags.Instance | BindingFlags.NonPublic, null, [settings], null)!;
        var window = new Window { Width = 380, Height = 240, ShowActivated = false, Opacity = 0 };
        try
        {
            window.Show();
            window.Activate();
            TrayMenuChecks.Pump();
            window.UpdateLayout();
            menu.PlacementTarget = window;
            menu.IsOpen = true;
            TrayMenuChecks.Pump();
            menu.UpdateLayout();
            Require(menu.Items.Count == 1 && menu.Placement == PlacementMode.Bottom, "single-item menu opens below its target");
            var checkbox = menu.FindName("StartupMenuItem") as MenuItem
                ?? throw new Exception("Settings checkbox missing");
            var error = menu.FindName("ErrorText") as TextBlock
                ?? throw new Exception("Settings error text missing");
            Require(checkbox.IsChecked == true && error.Visibility == Visibility.Collapsed,
                "menu initially displays default-on checkbox without an error");
            Require(checkbox.StaysOpenOnClick && menu.IsOpen, "toggle keeps menu open");
            Require(HasRoundedBorder(checkbox), "settings checkbox renders a custom rounded border");
            checkbox.IsChecked = false;
            checkbox.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var reopened = new SettingsService(path, _ => { });
            Require(!settings.StartWithWindows && checkbox.IsChecked == false && reopened.Load() && !reopened.StartWithWindows,
                "checkbox click immediately persists disabled startup");
            rejectEnable = true;
            checkbox.IsChecked = true;
            checkbox.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Require(checkbox.IsChecked == false && !settings.StartWithWindows &&
                error.Visibility == Visibility.Visible && error.Text.Contains("simulated UI registration failure"),
                "failed checkbox toggle rolls back the control and displays the registration error");
            reopened = new SettingsService(path, _ => { });
            Require(reopened.Load() && !reopened.StartWithWindows, "failed UI toggle preserves persisted disabled startup");
            rejectEnable = false;
            menu.IsOpen = false;
            window.Activate();
            TrayMenuChecks.Pump();
            menu.IsOpen = true;
            TrayMenuChecks.Pump();
            checkbox.Focus();
            TrayMenuChecks.Press(checkbox, Key.Enter);
            TrayMenuChecks.Pump();
            Require(menu.IsOpen && settings.StartWithWindows && checkbox.IsChecked,
                "keyboard Enter toggles startup and keeps the menu open");
            TrayMenuChecks.Press(menu, Key.Escape);
            TrayMenuChecks.Pump();
            Require(!menu.IsOpen, "Escape dismisses settings menu");
            TrayMenuChecks.OutsideClick(() => menu.IsOpen = true, menu);
            Require(!menu.IsOpen, "actual outside click dismisses settings menu");
            menu.IsOpen = true;
            TrayMenuChecks.Pump();
            Require(checkbox.IsChecked && error.Visibility == Visibility.Collapsed,
                "reopened menu shows saved state and clears obsolete errors");

        }
        finally { menu.IsOpen = false; window.Close(); }
    }

    private static bool HasRoundedBorder(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Border { CornerRadius: var radius } && radius == new CornerRadius(5)) return true;
            if (HasRoundedBorder(child)) return true;
        }
        return false;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("Settings check failed: " + message);
    }
}
