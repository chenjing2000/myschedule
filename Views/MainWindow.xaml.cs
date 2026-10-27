using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OneSchedule;

public partial class MainWindow : Window
{
    private enum CalendarLevel { Dates, Months, Years }

    private readonly ScheduleService _scheduleService = new();
    private readonly DispatcherTimer _dateTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private ReminderService? _reminderService;
    private DateOnly _displayMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private CalendarLevel _level;
    private int _browseYear;
    private int _decadeStart;
    private int _wheelRemainder;
    private DateTime _headerDate;
    private bool _reminderFlashDark;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        PreviewMouseDown += MainWindow_PreviewMouseDown;
        _dateTimer.Tick += DateTimer_Tick;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        const int windowStyle = -16;
        const int maximizeBox = 0x00010000;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        // Keep the resize border and minimize button while disabling native maximization.
        SetWindowLong(handle, windowStyle, GetWindowLong(handle, windowStyle) & ~maximizeBox);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_scheduleService.Reload()) ShowStatus("读取日程失败：" + _scheduleService.LastError);
        UpdateToday();
        NavigateTo(_displayMonth.Year, _displayMonth.Month);
        _dateTimer.Start();
        _reminderService = new ReminderService(_scheduleService, this, SetReminderFlash, OnReminderDue, ShowStatus);
    }

    private void DateTimer_Tick(object? sender, EventArgs e)
    {
        if (_headerDate == DateTime.Today) return;
        UpdateToday();
        RefreshView();
    }

    private void UpdateToday()
    {
        _headerDate = DateTime.Today;
        TodaySolarText.Text = _headerDate.ToString("M月d日，dddd", CultureInfo.GetCultureInfo("zh-CN"));
        LunarInfoText.Text = Lunar.GetDateText(_headerDate);
    }

    private void TodayButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateToday();
        Calendar.SelectDate(_headerDate);
        NavigateTo(_headerDate.Year, _headerDate.Month);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_scheduleService.Reload())
        {
            ShowStatus("读取日程失败：" + _scheduleService.LastError);
            return;
        }
        StatusBar.Visibility = Visibility.Collapsed;
        RefreshView();
    }

    private void Calendar_DayInvoked(object? sender, CalendarDayEventArgs e)
    {
        if (_reminderService?.IsAlertActive == true) return;
        if (DayEventsWindow.ShowForDate(this, e.Date, _scheduleService)) RebuildCalendar();
    }

    private void NavigateTo(int year, int month)
    {
        _displayMonth = new DateOnly(year, month, 1);
        _browseYear = year;
        _decadeStart = year / 10 * 10;
        _level = CalendarLevel.Dates;
        _wheelRemainder = 0;
        RefreshView();
    }

    private void PeriodButton_Click(object sender, RoutedEventArgs e)
    {
        if (_level == CalendarLevel.Dates)
        {
            _browseYear = _displayMonth.Year;
            _level = CalendarLevel.Months;
        }
        else if (_level == CalendarLevel.Months)
        {
            _decadeStart = _browseYear / 10 * 10;
            _level = CalendarLevel.Years;
        }
        _wheelRemainder = 0;
        RefreshView();
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e) => MovePeriod(-1);
    private void NextButton_Click(object sender, RoutedEventArgs e) => MovePeriod(1);

    private bool CanMove(int direction) => _level switch
    {
        CalendarLevel.Dates => direction < 0
            ? _displayMonth > new DateOnly(Lunar.MinYear, 1, 1)
            : _displayMonth < new DateOnly(Lunar.MaxYear, 12, 1),
        CalendarLevel.Months => direction < 0 ? _browseYear > Lunar.MinYear : _browseYear < Lunar.MaxYear,
        _ => direction < 0 ? _decadeStart > Lunar.MinYear / 10 * 10 : _decadeStart < Lunar.MaxYear / 10 * 10
    };

    private void MovePeriod(int direction)
    {
        if (!CanMove(direction)) return;
        switch (_level)
        {
            case CalendarLevel.Dates: _displayMonth = _displayMonth.AddMonths(direction); break;
            case CalendarLevel.Months: _browseYear += direction; break;
            case CalendarLevel.Years: _decadeStart += direction * 10; break;
        }
        RefreshView();
    }

    private void CalendarArea_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_reminderService?.IsAlertActive == true) { e.Handled = true; return; }
        // Accumulate high-resolution wheel deltas; one standard notch is 120.
        _wheelRemainder += e.Delta;
        while (Math.Abs(_wheelRemainder) >= 120)
        {
            int direction = _wheelRemainder > 0 ? -1 : 1;
            _wheelRemainder += direction * 120;
            MovePeriod(direction);
        }
        e.Handled = true;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (_reminderService?.IsAlertActive == true) DismissReminder();
        else if (_level == CalendarLevel.Years) _level = CalendarLevel.Months;
        else if (_level == CalendarLevel.Months) _level = CalendarLevel.Dates;
        else return;
        _wheelRemainder = 0;
        RefreshView();
        e.Handled = true;
    }

    private void RefreshView()
    {
        bool dates = _level == CalendarLevel.Dates;
        Calendar.Visibility = dates ? Visibility.Visible : Visibility.Collapsed;
        PeriodItems.Visibility = dates ? Visibility.Collapsed : Visibility.Visible;
        PeriodItems.Items.Clear();
        PeriodButton.Content = _level switch
        {
            CalendarLevel.Dates => $"{_displayMonth.Year}年{_displayMonth.Month}月",
            CalendarLevel.Months => $"{_browseYear}年",
            _ => $"{_decadeStart}－{_decadeStart + 9}"
        };
        PeriodButton.ToolTip = _level switch
        {
            CalendarLevel.Dates => "选择月份",
            CalendarLevel.Months => "选择年份",
            _ => "滚动或使用箭头切换十年"
        };
        PreviousButton.IsEnabled = CanMove(-1);
        NextButton.IsEnabled = CanMove(1);
        string unit = _level == CalendarLevel.Dates ? "个月" : _level == CalendarLevel.Months ? "年" : "个十年";
        PreviousButton.ToolTip = "上一" + unit;
        NextButton.ToolTip = "下一" + unit;
        System.Windows.Automation.AutomationProperties.SetName(PreviousButton, "上一" + unit);
        System.Windows.Automation.AutomationProperties.SetName(NextButton, "下一" + unit);
        if (dates) { RebuildCalendar(); return; }

        var today = DateTime.Today;
        if (_level == CalendarLevel.Months)
        {
            for (int month = 1; month <= 12; month++)
                AddPeriodChoice(month, $"{month}月", true,
                    _browseYear == _displayMonth.Year && month == _displayMonth.Month,
                    _browseYear == today.Year && month == today.Month);
        }
        else
        {
            for (int year = _decadeStart - 2; year <= _decadeStart + 13; year++)
                AddPeriodChoice(year, year.ToString(), year >= _decadeStart && year <= _decadeStart + 9,
                    year == _browseYear, year == today.Year);
        }
    }

    private void AddPeriodChoice(int value, string text, bool inPeriod, bool selected, bool today)
    {
        var button = new Button
        {
            Content = text, Tag = value,
            Style = (Style)FindResource("PeriodChoiceStyle"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Foreground = _reminderFlashDark ? Brushes.WhiteSmoke : (Brush)FindResource(selected ? "TodayTextBrush" : inPeriod ? "TextPrimaryBrush" : "OtherMonthBrush"),
            Background = selected ? (Brush)FindResource("AccentBrush") : Brushes.Transparent,
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(today && !selected ? 1 : 0),
            IsEnabled = _level != CalendarLevel.Years || value >= Lunar.MinYear && value <= Lunar.MaxYear
        };
        button.Click += PeriodChoice_Click;
        PeriodItems.Items.Add(button);
    }

    private void PeriodChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int value }) return;
        if (_level == CalendarLevel.Years)
        {
            _browseYear = value;
            _level = CalendarLevel.Months;
            _wheelRemainder = 0;
            RefreshView();
        }
        else NavigateTo(_browseYear, value);
    }

    private void RebuildCalendar() => Calendar.Rebuild(_displayMonth.Year, _displayMonth.Month, _scheduleService.Items);

    private void ShowStatus(string text)
    {
        HintText.Text = text;
        StatusBar.Visibility = Visibility.Visible;
    }

    private void SetReminderFlash(bool dark)
    {
        _reminderFlashDark = dark;
        Calendar.SetReminderFlash(dark);
        CalendarSurface.Background = dark ? new SolidColorBrush(Color.FromRgb(63, 70, 82)) : (Brush)FindResource("SurfaceBrush");
        if (_level != CalendarLevel.Dates) RefreshView();
    }

    private void OnReminderDue(ScheduleItem item)
    {
        ReminderText.Text = $"提醒：{item.EventName}　{item.Time}" +
                            (string.IsNullOrWhiteSpace(item.Location) ? "" : $"　·　{item.Location}") +
                            "　（点击关闭提醒）";
        ReminderBar.Visibility = Visibility.Visible;
    }

    private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_reminderService?.IsAlertActive != true) return;
        DismissReminder();
        e.Handled = true;
    }

    private void ReminderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DismissReminder();

    private void DismissReminder()
    {
        if (_reminderService?.DismissAlert() != true) return;
        ReminderBar.Visibility = Visibility.Collapsed;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _dateTimer.Stop();
        _dateTimer.Tick -= DateTimer_Tick;
        _reminderService?.Dispose();
    }
}
