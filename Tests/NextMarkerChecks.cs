using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using OneSchedule;

internal static class NextMarkerChecks
{
    public static void Run()
    {
        var date = DateTime.Today.AddDays(1);
        var early = Item(date, RepeatPeriod.Daily, 8);
        var late = Item(date, RepeatPeriod.Yearly, 16);
        var view = new CalendarView();
        view.Rebuild(date.Year, date.Month, [late, early]);
        var day = Day(view, date);
        Require(day.IndicatorRecurrence == RepeatPeriod.Daily, "first pending event follows event time");
        early.LastRemindedOccurrenceDate = DateOnly.FromDateTime(date);
        view.Rebuild(date.Year, date.Month, [late, early]);
        day = Day(view, date);
        Require(day.IndicatorRecurrence == RepeatPeriod.Yearly, "recorded reminder advances marker after rebuild");
        early.LastRemindedOccurrenceDate = null;
        Refresh(view, date.AddHours(7));
        Require(day.IndicatorRecurrence == RepeatPeriod.Daily, "rollback leaves earlier event pending");
        Layout(view);
        var marker = Children<Polygon>(view).Single(p => ReferenceEquals(p.DataContext, day));
        early.LastRemindedOccurrenceDate = DateOnly.FromDateTime(date);
        Refresh(view, date.AddHours(7));
        Layout(view);
        Require(((SolidColorBrush)marker.Fill).Color == (Color)ColorConverter.ConvertFromString("#F45D75"), "existing bound polygon updates without rebuilding date cells");
        Require(day.HasSchedule && day.Schedules.Count == 2 && day.TooltipText!.Contains("Daily") && day.TooltipText.Contains("Yearly"), "all events remain in tooltip and schedule list");
        late.LastRemindedOccurrenceDate = DateOnly.FromDateTime(date);
        Refresh(view, date.AddHours(7));
        Require(day.IndicatorRecurrence == RepeatPeriod.Yearly && marker.Visibility == Visibility.Visible, "all reminded retains last event marker");
        early.LastRemindedOccurrenceDate = DateOnly.FromDateTime(date.AddDays(-1));
        Refresh(view, date.AddHours(7));
        Require(day.IndicatorRecurrence == RepeatPeriod.Daily, "reminder for another occurrence does not suppress this date");
        late.LastRemindedOccurrenceDate = null;
        Refresh(view, date.AddHours(12));
        Require(day.IndicatorRecurrence == RepeatPeriod.Yearly, "expired morning event yields to upcoming afternoon event");
        Refresh(view, date.AddHours(8).AddMinutes(2));
        Require(day.IndicatorRecurrence == RepeatPeriod.Daily, "unrecorded reminder within grace period stays eligible");
        early.RemindingMinutes = 60;
        Refresh(view, date.AddHours(8).AddMinutes(2));
        Require(day.IndicatorRecurrence == RepeatPeriod.Yearly, "grace uses reminder time rather than event time");
        Refresh(view, date.AddHours(7).AddMinutes(30));
        Require(day.IndicatorRecurrence == RepeatPeriod.Daily, "upcoming event remains eligible after early reminder window");
        Refresh(view, date.AddHours(17));
        Require(day.IndicatorRecurrence == RepeatPeriod.Yearly, "all elapsed retains last event marker");
        var sameTime = Item(date, RepeatPeriod.Weekly, 8);
        view.Rebuild(date.Year, date.Month, [early, sameTime]);
        Refresh(view, date.AddHours(7));
        Require(Day(view,date).IndicatorRecurrence == RepeatPeriod.Daily, "equal times preserve source order");
        early.LastRemindedOccurrenceDate = DateOnly.FromDateTime(date);
        Refresh(view, date.AddHours(7));
        Require(Day(view,date).IndicatorRecurrence == RepeatPeriod.Weekly, "equal-time reminders advance separately");
        view.Rebuild(date.Year, date.Month, []);
        Require(Day(view,date).IndicatorRecurrence is null && !Day(view,date).HasSchedule, "empty date keeps marker hidden");
        var untimed = Item(date, RepeatPeriod.None, 0);
        untimed.EventTime = null;
        var scheduled = Item(date, RepeatPeriod.Yearly, 16);
        view.Rebuild(date.Year,date.Month,[untimed,scheduled]);
        Refresh(view,date.AddHours(7));
        Require(Day(view,date).IndicatorRecurrence == RepeatPeriod.Yearly, "untimed event does not hide an upcoming timed event");
        Refresh(view,date.AddHours(17));
        Require(Day(view,date).IndicatorRecurrence == RepeatPeriod.None, "untimed event remains visible when no timed event is pending");
        var first = Item(date, RepeatPeriod.Daily, 8);
        var second = Item(date, RepeatPeriod.Yearly, 16);
        first.LastRemindedOccurrenceDate = DateOnly.FromDateTime(date);
        view.Rebuild(date.Year, date.Month, ScheduleJson.Parse(ScheduleJson.Serialize([first, second])));
        Require(Day(view,date).IndicatorRecurrence == RepeatPeriod.Yearly, "persisted reminder state still selects the next event after reload");
        first.LastRemindedOccurrenceDate = null;
        var main = new MainWindow();
        try
        {
            var calendar = (CalendarView)main.FindName("Calendar");
            calendar.Rebuild(date.Year,date.Month,[first,second]);
            var liveDay = Day(calendar,date);
            first.LastRemindedOccurrenceDate = DateOnly.FromDateTime(date);
            typeof(MainWindow).GetMethod("OnReminderDue", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main,[first]);
            Require(liveDay.IndicatorRecurrence == RepeatPeriod.Yearly, "successful reminder callback updates the existing main calendar");
        }
        finally { main.Close(); }
        Console.WriteLine("PASS: next-event marker, reminder completion, grace, occurrence identity, stable ties and live polygon updates");
    }
    private static ScheduleItem Item(DateTime date, RepeatPeriod recurrence, int hour) => new()
    {
        Id = Guid.NewGuid().ToString(), Calendar = CalendarKind.Solar,
        StartDate = new ScheduleDate { Year = date.Year, Month = date.Month, Day = date.Day },
        Recurrence = recurrence, RepeatCount = ScheduleItem.DefaultRepeatCount(recurrence),
        EventTime = new TimeOnly(hour,0), EventName = recurrence.ToString()
    };
    private static CalendarDayViewModel Day(CalendarView view, DateTime date) => ((ItemsControl)view.FindName("DaysItems")).Items.Cast<CalendarDayViewModel>().Single(day => day.Date == date);
    private static void Refresh(CalendarView view, DateTime now) => view.RefreshIndicators(now);
    private static void Layout(FrameworkElement view) { view.Measure(new Size(344,320)); view.Arrange(new Rect(0,0,344,320)); view.UpdateLayout(); }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i=0; i<VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);
            if (child is T match) yield return match;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }
}
