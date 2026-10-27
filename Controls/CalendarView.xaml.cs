using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;

namespace OneSchedule;

public sealed class CalendarDayViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        internal set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public required DateTime Date { get; init; }
    public required int SolarDay { get; init; }
    public required string LunarText { get; init; }
    public required bool IsCurrentMonth { get; init; }
    public required bool IsToday { get; init; }
    public required bool IsWeekend { get; init; }
    public required bool HasSchedule { get; init; }
    public bool IsSupportedDate => Date.Year >= Lunar.MinYear && Date.Year <= Lunar.MaxYear;
    public required IReadOnlyList<ScheduleItem> Schedules { get; init; }
    public RepeatPeriod? IndicatorRecurrence => Schedules.FirstOrDefault()?.Recurrence;
    public string? TooltipText => Schedules.Count == 0
        ? null
        : string.Join(Environment.NewLine, Schedules.Select(FormatSchedule));

    private static string FormatSchedule(ScheduleItem schedule) =>
        schedule.Time + "  " + schedule.EventName +
        (string.IsNullOrEmpty(schedule.Location) ? "" : "（" + schedule.Location + "）");
}

public sealed class CalendarDayEventArgs(DateTime date) : EventArgs
{
    public DateTime Date { get; } = date;
}

public partial class CalendarView : UserControl
{
    public static readonly DependencyProperty IsReminderFlashDarkProperty = DependencyProperty.Register(
        nameof(IsReminderFlashDark), typeof(bool), typeof(CalendarView), new PropertyMetadata(false));

    public CalendarView() => InitializeComponent();

    public bool IsReminderFlashDark
    {
        get => (bool)GetValue(IsReminderFlashDarkProperty);
        private set => SetValue(IsReminderFlashDarkProperty, value);
    }

    public event EventHandler<CalendarDayEventArgs>? DayInvoked;
    public DateTime SelectedDate { get; private set; } = DateTime.Today;

    public void SelectDate(DateTime date)
    {
        if (date.Year < Lunar.MinYear || date.Year > Lunar.MaxYear) return;
        SelectedDate = date.Date;
        foreach (CalendarDayViewModel day in DaysItems.Items)
            day.IsSelected = day.Date == SelectedDate;
    }

    public void Rebuild(int year, int month, IReadOnlyList<ScheduleItem> schedules)
    {
        var first = new DateTime(year, month, 1);
        int mondayOffset = ((int)first.DayOfWeek + 6) % 7;
        var gridStart = first.AddDays(-mondayOffset);
        const int weeks = 6;

        var days = new List<CalendarDayViewModel>(weeks * 7);
        for (int i = 0; i < weeks * 7; i++)
        {
            var date = gridStart.AddDays(i);
            var list = schedules.Where(item => ScheduleOccurrence.IsOnDate(item, date)).OrderBy(item => item.EventTime).ToList();
            days.Add(new CalendarDayViewModel
            {
                Date = date,
                SolarDay = date.Day,
                LunarText = SafeLunarText(date),
                IsCurrentMonth = date.Year == year && date.Month == month,
                IsToday = date.Date == DateTime.Today,
                IsSelected = date == SelectedDate,
                IsWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                HasSchedule = list.Count > 0,
                Schedules = list
            });
        }

        DaysItems.ItemsSource = days;
    }

    public void SetReminderFlash(bool dark) => IsReminderFlashDark = dark;

    private void DayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CalendarDayViewModel { IsSupportedDate: true } day })
            SelectDate(day.Date);
    }

    private void DayButton_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            sender is not Button { DataContext: CalendarDayViewModel { IsSupportedDate: true } day }) return;
        SelectDate(day.Date);
        e.Handled = true;
        DayInvoked?.Invoke(this, new CalendarDayEventArgs(day.Date));
    }

    private static string SafeLunarText(DateTime date)
    {
        try { return Lunar.GetLunarText(date); }
        catch { return ""; }
    }
}
