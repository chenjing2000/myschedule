using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OneSchedule;

public partial class ScheduleEditWindow : Window
{
    private readonly ScheduleItem? _editing;
    private readonly DateTime _defaultDate;
    private bool _updating;
    private CalendarKind _calendar;
    private ScheduleDate _date = new();
    private ScheduleItem? _result;
    private sealed record DateOption(int Value, string Text, bool IsLeap = false);

    private ScheduleEditWindow(DateTime defaultDate, ScheduleItem? existing)
    {
        InitializeComponent();
        _defaultDate = defaultDate.Date;
        _editing = existing;
        Title = existing is null ? "新增日程" : "编辑日程";
        DataObject.AddPastingHandler(RemindMinutesTextBox, IntegerTextBox_Pasting);
        DataObject.AddPastingHandler(RepeatCountTextBox, IntegerTextBox_Pasting);
        Loaded += ScheduleEditWindow_Loaded;
    }

    public static ScheduleItem? ShowEditor(Window owner, DateTime defaultDate, ScheduleItem? existing)
    {
        var dialog = new ScheduleEditWindow(defaultDate, existing) { Owner = owner, Width = owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width };
        return dialog.ShowDialog() == true ? dialog._result : null;
    }

    private void ScheduleEditWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _calendar = _editing?.Calendar ?? CalendarKind.Solar;
        _date = _editing?.StartDate ?? new ScheduleDate { Year = _defaultDate.Year, Month = _defaultDate.Month, Day = _defaultDate.Day };
        var recurrence = _editing?.Recurrence ?? RepeatPeriod.None;
        RecurrenceCombo.SelectedValue = recurrence.ToString();
        RepeatCountTextBox.Text = (_editing?.RepeatCount ?? ScheduleItem.DefaultRepeatCount(recurrence)).ToString();
        HourCombo.ItemsSource = Enumerable.Range(0, 24).Select(TwoDigits).ToList();
        MinuteCombo.ItemsSource = Enumerable.Range(0, 60).Select(TwoDigits).ToList();
        var time = _editing?.EventTime ?? TimeOnly.FromDateTime(DateTime.Now.AddMinutes(5));
        HourCombo.SelectedItem = TwoDigits(time.Hour);
        MinuteCombo.SelectedItem = TwoDigits(time.Minute);
        EventNameTextBox.Text = _editing?.EventName ?? "";
        LocationTextBox.Text = _editing?.Location ?? "";
        RemindMinutesTextBox.Text = (_editing?.RemindingMinutes ?? 0).ToString();
        RefreshDateOptions();
    }

    private void EditorGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double width = Math.Max(0, Math.Floor(EditorGrid.ColumnDefinitions[1].ActualWidth));
        EventNameTextBox.Width = LocationTextBox.Width = width;
    }

    private void RecurrenceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RepeatCountTextBox is null || !Enum.TryParse<RepeatPeriod>(RecurrenceCombo.SelectedValue as string, out var period)) return;
        RepeatCountTextBox.IsEnabled = period != RepeatPeriod.None;
        RepeatCountTextBox.Text = ScheduleItem.DefaultRepeatCount(period).ToString();
    }

    private void CalendarTypeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DateTime solar = _date.ToSolar(_calendar);
            CalendarKind next = _calendar == CalendarKind.Solar ? CalendarKind.Lunar : CalendarKind.Solar;
            ScheduleDate converted;
            if (next == CalendarKind.Lunar)
            {
                var lunar = Lunar.FromSolar(solar);
                converted = new ScheduleDate { Year = lunar.Year, Month = lunar.Month, Day = lunar.Day, IsLeapMonth = lunar.IsLeap };
            }
            else converted = new ScheduleDate { Year = solar.Year, Month = solar.Month, Day = solar.Day };
            _ = converted.ToSolar(next);
            _calendar = next;
            _date = converted;
            ValidationText.Text = "";
            RefreshDateOptions();
        }
        catch (ArgumentException ex) { ValidationText.Text = "无法切换历法：" + ex.Message; }
    }

    private void RefreshDateOptions()
    {
        _updating = true;
        try
        {
            bool lunar = _calendar == CalendarKind.Lunar;
            CalendarTypeButton.Content = lunar ? "阴历" : "阳历";
            YearCombo.ItemsSource = Enumerable.Range(Lunar.MinYear, Lunar.MaxYear - Lunar.MinYear + 1)
                .Select(y => new DateOption(y, lunar ? ChineseYear(y) : y.ToString())).ToList();
            YearCombo.SelectedValue = _date.Year;
            int leap = lunar ? Lunar.LeapMonthOf(_date.Year) : 0;
            var months = new List<DateOption>();
            for (int m = 1; m <= 12; m++)
            {
                months.Add(new DateOption(m, lunar ? Lunar.GetMonthText(m, false)[..^1] : m.ToString()));
                if (m == leap) months.Add(new DateOption(m, Lunar.GetMonthText(m, true)[..^1], true));
            }
            var selected = months.FirstOrDefault(m => m.Value == _date.Month && m.IsLeap == _date.IsLeapMonth)
                ?? months.First(m => m.Value == _date.Month && !m.IsLeap);
            MonthCombo.ItemsSource = months;
            MonthCombo.SelectedItem = selected;
            int maxDay = !lunar ? DateTime.DaysInMonth(_date.Year, selected.Value)
                : selected.IsLeap ? Lunar.DaysInLeapMonth(_date.Year, selected.Value) : Lunar.DaysInLunarMonth(_date.Year, selected.Value);
            DayCombo.ItemsSource = Enumerable.Range(1, maxDay)
                .Select(d => new DateOption(d, lunar ? Lunar.GetDayText(d) : d.ToString())).ToList();
            int day = Math.Min(_date.Day, maxDay);
            DayCombo.SelectedValue = day;
            _date = new ScheduleDate { Year = _date.Year, Month = selected.Value, Day = day, IsLeapMonth = selected.IsLeap };
        }
        finally { _updating = false; }
    }

    private void DatePart_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || YearCombo.SelectedValue is not int year ||
            MonthCombo.SelectedItem is not DateOption month || DayCombo.SelectedValue is not int day) return;
        _date = new ScheduleDate { Year = year, Month = month.Value, Day = day, IsLeapMonth = month.IsLeap };
        ValidationText.Text = "";
        RefreshDateOptions();
    }

    private static string ChineseYear(int year) => string.Concat(year.ToString(CultureInfo.InvariantCulture).Select(c => "〇一二三四五六七八九"[c - '0']));
    private static string TwoDigits(int value) => value.ToString("D2");

    private void RemindMinutesTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        RemindMinutesTextBox.Focus();
        RemindMinutesTextBox.Select(RemindMinutesTextBox.Text.Length, 0);
        e.Handled = true;
    }

    private void IntegerTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = string.IsNullOrEmpty(e.Text) || !e.Text.All(c => c is >= '0' and <= '9');

    private void IntegerTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        string pasted = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? "";
        var box = (TextBox)sender;
        int length = box.Text.Length - box.SelectionLength + pasted.Length;
        if (pasted.Length == 0 || !pasted.All(c => c is >= '0' and <= '9') || length > box.MaxLength) e.CancelCommand();
    }

    private bool TryBuildResult(out ScheduleItem result, out string error)
    {
        result = new ScheduleItem();
        error = "";
        if (HourCombo.SelectedItem is not string hour || MinuteCombo.SelectedItem is not string minute)
        { error = "请选择完整时间。"; return false; }
        if (!Enum.TryParse<RepeatPeriod>(RecurrenceCombo.SelectedValue as string, out var recurrence))
        { error = "请选择周期。"; return false; }
        if (!int.TryParse(RemindMinutesTextBox.Text.Trim(), out int reminder) || reminder is < 0 or > 99)
        { error = "提前提醒分钟数必须是0～99的整数。"; return false; }
        int repeatCount = 1;
        if (recurrence != RepeatPeriod.None && (RepeatCountTextBox.Text.Length is < 1 or > 3 ||
            !RepeatCountTextBox.Text.All(c => c is >= '0' and <= '9') ||
            !int.TryParse(RepeatCountTextBox.Text, out repeatCount) || repeatCount is < 1 or > 999))
        { error = "重复次数必须是1～999的整数。"; return false; }
        result = new ScheduleItem
        {
            RepeatCount = repeatCount,
            Id = _editing?.Id ?? Guid.NewGuid().ToString(), Calendar = _calendar, StartDate = _date,
            Recurrence = recurrence, EventTime = new TimeOnly(int.Parse(hour), int.Parse(minute)),
            EventName = EventNameTextBox.Text.Trim(), Location = LocationTextBox.Text.Trim(), RemindingMinutes = reminder
        };
        if (_editing is not null && ScheduleOccurrence.SameReminderIdentity(_editing, result))
            result.LastRemindedOccurrenceDate = _editing.LastRemindedOccurrenceDate;
        try { ScheduleJson.Validate(result); return true; }
        catch (ArgumentException ex) { error = "日期无效：" + ex.Message; return false; }
        catch (FormatException ex) { error = ex.Message; return false; }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildResult(out var result, out string error)) { ValidationText.Text = error; return; }
        _result = result;
        DialogResult = true;
    }

}
