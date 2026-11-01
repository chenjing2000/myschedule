using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;

namespace OneSchedule;

public partial class DayEventsWindow : Window
{
    private readonly DateTime _date;
    private readonly ScheduleService _scheduleService;
    private List<ScheduleItem> _dayItems = [];
    private bool _changed;

    private DayEventsWindow(DateTime date, ScheduleService scheduleService)
    {
        InitializeComponent();
        _date = date.Date;
        _scheduleService = scheduleService;
        DateText.Text = FormatDateHeader(_date);
        RefreshList();
    }

    public static bool ShowForDate(Window owner, DateTime date, ScheduleService scheduleService)
    {
        var dialog = new DayEventsWindow(date, scheduleService) { Owner = owner, Width = owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width };
        dialog.ShowDialog();
        return dialog._changed;
    }

    private static string FormatDateHeader(DateTime date)
    {
        string weekday = date.ToString("dddd", CultureInfo.GetCultureInfo("zh-CN"));
        try { return $"{date:yyyy年M月d日}（{Lunar.GetDateText(date)}） {weekday}"; }
        catch { return $"{date:yyyy年M月d日} {weekday}"; }
    }

    private void RefreshList()
    {
        _dayItems = _scheduleService.GetForDate(_date);
        ScheduleListBox.ItemsSource = null;
        ScheduleListBox.ItemsSource = _dayItems;
        ScheduleListBox.SelectedIndex = -1;
        DeleteButton.IsEnabled = false;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (ScheduleEditWindow.ShowEditor(this, _date, null) is { } item)
            ApplyChange(() => _scheduleService.Add(item));
    }

    private void ScheduleListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source) return;
        // Scrollbar gestures are navigation, not empty-list gestures.
        for (DependencyObject? current = source; current is not null && current != ScheduleListBox;
             current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is ScrollBar) return;
        e.Handled = true;
        if (ItemsControl.ContainerFromElement(ScheduleListBox, source) is ListBoxItem { Content: ScheduleItem item })
            EditItem(item);
        else
            AddButton_Click(sender, e);
    }

    private void EditItem(ScheduleItem item)
    {
        if (ScheduleEditWindow.ShowEditor(this, _date, item) is not { } updated) return;
        ApplyChange(() => _scheduleService.Update(item, updated));
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (ScheduleListBox.SelectedItem is not ScheduleItem item) return;
        ApplyChange(() => _scheduleService.Delete(item));
    }

    private void ApplyChange(Func<bool> change)
    {
        if (!change())
        {
            if (!string.IsNullOrWhiteSpace(_scheduleService.LastError)) ShowSaveError();
            return;
        }

        _changed = true;
        RefreshList();
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        // The command needs the selected target until its Click handler executes.
        for (var current=source; current is not null;
             current=current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current==DeleteButton && e.ChangedButton==MouseButton.Left) return;
        if (ItemsControl.ContainerFromElement(ScheduleListBox,source) is ListBoxItem row &&
            ReferenceEquals(row.Content,ScheduleListBox.SelectedItem)) return;
        ScheduleListBox.UnselectAll();
        Focus();
    }

    private void Window_Deactivated(object? sender, EventArgs e) => ScheduleListBox.UnselectAll();

    private void ScheduleListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        DeleteButton.IsEnabled = ScheduleListBox.SelectedItem is not null;

    private void ShowSaveError() =>
        MessageBox.Show(this, "保存日程失败：" + _scheduleService.LastError, "MySchedule", MessageBoxButton.OK, MessageBoxImage.Error);
}
