using System.Windows;

namespace OneSchedule;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;

    private SettingsWindow(SettingsService settings)
    {
        InitializeComponent();
        _settings = settings;
        StartupCheckBox.IsChecked = settings.StartWithWindows;
        ShowError();
    }

    public static void ShowSettings(Window owner, SettingsService settings)
    {
        var dialog = new SettingsWindow(settings)
        {
            Owner = owner,
            Width = owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width
        };
        dialog.ShowDialog();
    }

    private void StartupCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _settings.SetStartWithWindows(StartupCheckBox.IsChecked == true);
        StartupCheckBox.IsChecked = _settings.StartWithWindows;
        ShowError();
    }

    private void ShowError()
    {
        ErrorText.Text = _settings.LastError;
        ErrorText.Visibility = string.IsNullOrEmpty(_settings.LastError) ? Visibility.Collapsed : Visibility.Visible;
    }
}
