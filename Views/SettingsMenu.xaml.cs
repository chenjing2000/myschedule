using System.Windows;
using System.Windows.Controls;

namespace OneSchedule;

public partial class SettingsMenu : ContextMenu
{
    private readonly SettingsService _settings;

    internal SettingsMenu(SettingsService settings)
    {
        InitializeComponent();
        _settings = settings;
        Opened += (_, _) => RefreshState();
        RefreshState();
    }

    private void StartupMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.SetStartWithWindows(StartupMenuItem.IsChecked);
        RefreshState();
    }

    private void RefreshState()
    {
        StartupMenuItem.IsChecked = _settings.StartWithWindows;
        ErrorText.Text = _settings.LastError;
        ErrorText.Visibility = string.IsNullOrEmpty(_settings.LastError) ? Visibility.Collapsed : Visibility.Visible;
    }
}
