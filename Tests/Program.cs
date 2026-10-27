using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using OneSchedule;

internal static class Program
{
    private static int _checks;
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--upgrade-v1") return DataUpgrade.UpgradeFile(args[1]);
        if (args.Contains("--instance-modal-host"))
        {
            var instanceApp = new OneSchedule.App();
            instanceApp.InitializeComponent();
            instanceApp.Startup += (_, _) => instanceApp.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    var day = new Window { Owner = instanceApp.MainWindow, Title = "Instance probe dialog", Width = 300, Height = 180, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                    day.Loaded += (_, _) => day.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                        new Action(() => new Window { Owner = day, Title = "Instance probe editor", Width = 280, Height = 150, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog()));
                    day.ShowDialog();
                }));
            return instanceApp.Run();
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/OneSchedule;component/Resources/Theme.xaml", UriKind.Relative)
        });
        var window = new MainWindow();
        try
        {
            WeekdayChecks.Run();
            ScrollBarChecks.Run();
            DigitMarkerChecks.Run();
            NumberInputChecks.Run();
            RepeatChecks.Run();
            ComboChecks.Run();
            InteractionChecks.Run();
            EventChecks.Run();
            // Invoke initialization without opening a native window or pumping reminder timers.
            Invoke(window, "MainWindow_Loaded", window, new RoutedEventArgs());
            Check(window.Width == 400 && window.Height == 460, "compact default window");
            Click(window, "PeriodButton");
            Check(Title(window) == $"{DateTime.Today.Year}年", "date -> months");
            Check(Choices(window).Items.Count == 12, "twelve months");
            Click(window, "PeriodButton");
            Check(Title(window).Contains("－"), "months -> years");
            Check(Choices(window).Items.Count == 16, "sixteen complete year cells");
            var title = Title(window);
            int selectedYear = SelectedValue(window);
            Wheel(window, 120);
            int previousDecade = DateTime.Today.Year / 10 * 10 - 10;
            Check(Title(window) == $"{previousDecade}－{previousDecade + 9}", "wheel moves exactly ten years");
            Check(SelectedValue(window) == -1 || SelectedValue(window) == selectedYear, "year highlight does not move with decade");
            Wheel(window, -120);
            Check(Title(window) == title, "wheel returns decade");
            Check(SelectedValue(window) == selectedYear, "year highlight restored unchanged");
            Escape(window);
            Check(Title(window) == $"{DateTime.Today.Year}年", "Esc -> months");
            Wheel(window, 120);
            Check(Title(window) == $"{DateTime.Today.Year - 1}年", "month wheel moves year");
            Escape(window);
            Check(Title(window) == $"{DateTime.Today.Year}年{DateTime.Today.Month}月", "Esc restores date view");

            Invoke(window, "NavigateTo", 2026, 12);
            Wheel(window, -120);
            Check(Title(window) == "2027年1月", "December crosses year");
            Wheel(window, 120);
            Check(Title(window) == "2026年12月", "January crosses year");
            Wheel(window, 60);
            Check(Title(window) == "2026年12月", "partial wheel notch accumulates");
            Wheel(window, 60);
            Check(Title(window) == "2026年11月", "two partial notches move one month");
            Wheel(window, -240);
            Check(Title(window) == "2027年1月", "multiple notches move multiple months");
            Invoke(window, "NavigateTo", 1901, 1);
            Check(!Find<Button>(window, "PreviousButton").IsEnabled, "minimum disables previous");
            Render(window, "minimum-dates");
            Check(Descendants<Button>(Find<CalendarView>(window, "Calendar")).Where(b => b.DataContext is CalendarDayViewModel d && d.Date.Year < 1901).All(b => !b.IsEnabled), "out-of-range date cells disabled");
            Wheel(window, 120);
            Check(Title(window) == "1901年1月", "minimum wheel stops");
            Click(window, "PeriodButton");
            Click(window, "PeriodButton");
            Check(!Find<Button>(window, "PreviousButton").IsEnabled, "minimum decade stops");
            Check(Choices(window).Items.Cast<Button>().Where(b => (int)b.Tag < 1901).All(b => !b.IsEnabled), "out-of-range years disabled");
            Flash(window, true);
            Check(((SolidColorBrush)Find<Grid>(window, "CalendarSurface").Background).Color.R < 100, "year view receives reminder flash");
            Check(Choices(window).Items.Cast<Button>().Where(b => b.IsEnabled).All(b => ((SolidColorBrush)b.Foreground).Color.R > 200), "year flash text readable");
            Flash(window, false);
            Render(window, "minimum-years");
            Pick(window, 1910);
            Check(Title(window) == "1910年", "valid adjacent decade year selectable");
            Flash(window, true);
            Check(((SolidColorBrush)Find<Grid>(window, "CalendarSurface").Background).Color.R < 100, "month view receives reminder flash");
            Flash(window, false);
            Invoke(window, "NavigateTo", 2100, 12);
            Check(!Find<Button>(window, "NextButton").IsEnabled, "maximum disables next");
            Wheel(window, -120);
            Check(Title(window) == "2100年12月", "maximum wheel stops");
            Click(window, "PeriodButton");
            Click(window, "PeriodButton");
            Check(!Find<Button>(window, "NextButton").IsEnabled, "maximum decade stops");
            Click(window, "TodayButton");
            Check(Title(window) == $"{DateTime.Today.Year}年{DateTime.Today.Month}月", "today returns from years");
            var selectionCalendar = Find<CalendarView>(window, "Calendar");
            selectionCalendar.SelectDate(DateTime.Today.AddDays(1));
            Click(window, "TodayButton");
            Check(selectionCalendar.SelectedDate == DateTime.Today, "Today also selects today's date");

            var calendar = Find<CalendarView>(window, "Calendar");
            var days = Find<ItemsControl>(calendar, "DaysItems");
            Check(days.Items.Count == 42, "fixed six weeks");
            var first = (CalendarDayViewModel)days.Items[0];
            Check(first.Date.DayOfWeek == DayOfWeek.Monday, "Monday first");
            Check(Find<TextBlock>(window, "TodaySolarText").Text.Contains("星期"), "today header includes weekday");
            string header = Find<TextBlock>(window, "TodaySolarText").Text;
            Render(window, "dates");
            Render(window, "dates-150pct", 144);
            Render(window, "dates-200pct", 192);
            var todayButton = Descendants<Button>(calendar).Single(b => b.DataContext is CalendarDayViewModel { IsToday: true });
            var circle = Descendants<Border>(todayButton).Single(b => b.Name == "DayCircle");
            Check(circle.ActualWidth == circle.ActualHeight, "today is a circle");
            Check(Descendants<TextBlock>(circle).Count() == 2, "both dates inside circle");
            Check(todayButton.ActualHeight >= circle.ActualHeight, "circle fits minimum window");
            Check(Descendants<TextBlock>(circle).Sum(t => t.ActualHeight) <= circle.ActualHeight, "both text lines fit circle");
            calendar.SetReminderFlash(true);
            Check(Descendants<TextBlock>(circle).All(t => ((SolidColorBrush)t.Foreground).Color.R > 200), "reminder flash retains readable dates");
            calendar.SetReminderFlash(false);
            calendar.Rebuild(DateTime.Today.Year, DateTime.Today.Month,
                [new ScheduleItem { StartDate = new ScheduleDate { Year = DateTime.Today.Year, Month = DateTime.Today.Month, Day = DateTime.Today.Day }, EventName = "Test appointment", EventTime = new TimeOnly(9, 30), Location = "Office" }]);
            window.UpdateLayout();
            var marked = Descendants<Button>(calendar).Single(b => b.DataContext is CalendarDayViewModel { IsToday: true });
            Check(marked.ToolTip?.ToString()?.Contains("09:30  Test appointment（Office）") == true, "schedule tooltip retained");
            Check(Descendants<System.Windows.Shapes.Polygon>(marked).Single().Visibility == Visibility.Visible, "schedule triangle retained");
            var emptyDay = Descendants<Button>(calendar).First(b => b.DataContext is CalendarDayViewModel { HasSchedule: false });
            Check(Descendants<System.Windows.Shapes.Polygon>(emptyDay).Single().Visibility == Visibility.Collapsed, "empty day has no triangle");
            DateTime? invoked = null;
            var isolatedCalendar = new CalendarView();
            isolatedCalendar.DayInvoked += (_, e) => invoked = e.Date;
            Invoke(isolatedCalendar, "DayButton_Click", marked, new RoutedEventArgs());
            Check(invoked is null && isolatedCalendar.SelectedDate == DateTime.Today, "single click selects without invoking");
            Invoke(isolatedCalendar, "DayButton_MouseDoubleClick", marked,
                new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent });
            Check(invoked == DateTime.Today, "double-click day invocation preserves date");
            Click(window, "PeriodButton");
            Render(window, "months");
            Check(Descendants<Button>(Choices(window)).All(b => b.ActualHeight == 60), "month circles fit minimum window");
            Pick(window, 3);
            Check(Title(window) == $"{DateTime.Today.Year}年3月", "month selection returns dates");
            Click(window, "PeriodButton");
            Click(window, "PeriodButton");
            Render(window, "years");
            Check(Descendants<Button>(Choices(window)).All(b => b.ActualHeight == 60), "year circles fit minimum window");
            Pick(window, 2024);
            Check(Title(window) == "2024年", "year selection returns months");
            Check(Find<TextBlock>(window, "TodaySolarText").Text == header, "browsing preserves today header");
            typeof(MainWindow).GetField("_headerDate", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, DateTime.Today.AddDays(-1));
            Invoke(window, "DateTimer_Tick", window, EventArgs.Empty);
            Check((DateTime)typeof(MainWindow).GetField("_headerDate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)! == DateTime.Today, "midnight tick refreshes today");
            Click(window, "TodayButton");
            Find<Border>(window, "ReminderBar").Visibility = Visibility.Visible;
            Find<TextBlock>(window, "ReminderText").Text = "提醒：较长的事项名称和地点，点击关闭提醒。较长的事项名称和地点，点击关闭提醒。";
            Render(window, "reminder");
            Check(Descendants<Button>(calendar).All(b => b.ActualHeight >= Descendants<Border>(b).Single(x => x.Name == "DayCircle").ActualHeight), "reminder does not overlap date circles");
            Find<Border>(window, "ReminderBar").Visibility = Visibility.Collapsed;
            // Exercise every supported month, including four/five/six-week months and both bounds.
            for (int year = 1901; year <= 2100; year++)
            for (int month = 1; month <= 12; month++)
            {
                calendar.Rebuild(year, month, []);
                var entries = days.Items.Cast<CalendarDayViewModel>().ToArray();
                if (entries.Length != 42 || entries[0].Date.DayOfWeek != DayOfWeek.Monday ||
                    !entries.Select((d, index) => d.Date == entries[0].Date.AddDays(index)).All(valid => valid) ||
                    entries.Count(d => d.IsCurrentMonth) != DateTime.DaysInMonth(year, month))
                    throw new Exception($"Invalid grid for {year}-{month}");
            }
            Check(true, "all 2400 supported months have complete continuous Monday-first grids");
            Console.WriteLine($"PASS: {_checks} checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { window.Close(); app.Shutdown(); }
    }

    private static T Find<T>(FrameworkElement parent, string name) where T : class =>
        parent.FindName(name) as T ?? throw new Exception($"Missing {name}");
    private static ItemsControl Choices(MainWindow w) => Find<ItemsControl>(w, "PeriodItems");
    private static int SelectedValue(MainWindow w) => Choices(w).Items.Cast<Button>()
        .Where(b => b.Background is SolidColorBrush brush && brush.Color.A != 0)
        .Select(b => (int)b.Tag).DefaultIfEmpty(-1).Single();
    private static string Title(MainWindow w) => (string)Find<Button>(w, "PeriodButton").Content;
    private static void Flash(MainWindow window, bool dark)
    {
        var service = typeof(MainWindow).GetField("_reminderService", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var callback = (Action<bool>)typeof(ReminderService).GetField("_setCalendarFlash", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        callback(dark);
    }
    private static void Click(MainWindow w, string name) => Find<Button>(w, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Invoke(object instance, string method, params object[] args) =>
        (instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Exception($"Missing {method}")).Invoke(instance, args);
    private static void Wheel(MainWindow w, int delta) => Invoke(w, "CalendarArea_MouseWheel", w,
        new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = UIElement.MouseWheelEvent });
    private static void Escape(MainWindow w) => Invoke(w, "MainWindow_PreviewKeyDown", w,
        new KeyEventArgs(Keyboard.PrimaryDevice, new FakeSource(), 0, Key.Escape) { RoutedEvent = UIElement.PreviewKeyDownEvent });
    private static void Pick(MainWindow w, int value)
    {
        w.UpdateLayout();
        var button = Descendants<Button>(Choices(w)).Single(b => (int)b.Tag == value);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        _checks++;
        Console.WriteLine("PASS: " + name);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var item in Descendants<T>(child)) yield return item;
        }
    }
    private static void Render(MainWindow window, string name, int dpi = 96)
    {
        // Approximate client area at the agreed minimum 380x440 outer size.
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(364, 401));
        content.Arrange(new Rect(0, 0, 364, 401));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(364 * dpi / 96, 401 * dpi / 96, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "previews"));
        using var file = File.Create(Path.Combine(AppContext.BaseDirectory, "previews", name + ".png"));
        encoder.Save(file);
    }
    private sealed class FakeSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
