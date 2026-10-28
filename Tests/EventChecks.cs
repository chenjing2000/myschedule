using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OneSchedule;

internal static class EventChecks
{
    private static int _checks;
    public static void Run()
    {
        JsonChecks();
        OccurrenceChecks();
        ReminderChecks();
        PersistenceChecks();
        EditorChecks();
        DialogChecks.Run();
        Console.WriteLine($"PASS: {_checks} event checks");
    }

    private static ScheduleItem Event(int year = 2026, int month = 10, int day = 26,
        RepeatPeriod recurrence = RepeatPeriod.None, CalendarKind calendar = CalendarKind.Solar, bool leap = false) => new()
    {
        Id = Guid.NewGuid().ToString(), EventName = "测试事项\"换行\n地点", Note = "办公室\\A",
        Calendar = calendar, StartDate = new ScheduleDate { Year = year, Month = month, Day = day, IsLeapMonth = leap },
        EventTime = new TimeOnly(9, 0), Recurrence = recurrence, RepeatCount = recurrence == RepeatPeriod.None ? 1 : 999, RemindingMinutes = 15
    };
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        _checks++;
        Console.WriteLine("PASS: " + name);
    }
    private static void Rejected(Action action, string name)
    {
        bool rejected = false;
        try { action(); }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException) { rejected = true; }
        Check(rejected, name);
    }

    private static void JsonChecks()
    {
        string empty = ScheduleJson.Serialize([]);
        using var document = JsonDocument.Parse(empty);
        Check(document.RootElement.GetProperty("schemaVersion").GetInt32() == 2 &&
            document.RootElement.GetProperty("events").GetArrayLength() == 0, "standard versioned empty JSON");
        var item = Event(2026, 9, 17, RepeatPeriod.Monthly, CalendarKind.Lunar);
        item.LastRemindedOccurrenceDate = DateOnly.FromDateTime(item.StartDate.ToSolar(item.Calendar));
        string text = ScheduleJson.Serialize([item]);
        var parsed = ScheduleJson.Parse(text).Single();
        Check(parsed.StartDate == item.StartDate && parsed.EventName == item.EventName && parsed.Note == item.Note &&
            parsed.Calendar == CalendarKind.Lunar && parsed.Recurrence == RepeatPeriod.Monthly && parsed.EventTime == item.EventTime &&
            parsed.LastRemindedOccurrenceDate == item.LastRemindedOccurrenceDate && parsed.Id == item.Id, "JSON full round trip including escaping and reminder date");
        Check(text.Contains("\"time\": \"09:00\"") && text.Contains("\"calendar\": \"lunar\""), "JSON uses HH:mm and named calendar");
        Rejected(() => ScheduleJson.Parse("[]"), "old array format rejected");
        Rejected(() => ScheduleJson.Parse(empty.Replace(": 2", ": 3")), "unknown schema rejected");
        Rejected(() => ScheduleJson.Parse(empty.Replace(": 2", ": null")), "null schema version rejected clearly");
        Rejected(() => ScheduleJson.Parse("{"), "malformed JSON rejected");
        Rejected(() => ScheduleJson.Parse("{\"schemaVersion\":2,\"events\":null}"), "null event list rejected");
        Rejected(() => ScheduleJson.Parse("{\"schemaVersion\":2,\"events\":[null]}"), "null event rejected");
        Rejected(() => ScheduleJson.Parse("{\"schemaVersion\":2,\"events\":[{}]}"), "missing required fields rejected");
        Rejected(() => ScheduleJson.Parse(text.Replace("\"monthly\"", "\"sometimes\"")), "unknown recurrence rejected");
        Rejected(() => ScheduleJson.Parse(text.Replace("\"monthly\"", "3")), "numeric recurrence rejected");
        Rejected(() => ScheduleJson.Parse(text.Replace("\"09:00\"", "\"9:00\"")), "noncanonical time rejected");
        Rejected(() => ScheduleJson.Parse(text.Replace("\"note\":", "\"unknown\":")), "unknown property rejected");
        Rejected(() => ScheduleJson.Serialize([item, item]), "duplicate IDs rejected");
        var invalid = Event(); invalid.RemindingMinutes = 100;
        Rejected(() => ScheduleJson.Serialize([invalid]), "reminder outside UI range rejected");
        invalid = Event(); invalid.StartDate = invalid.StartDate with { IsLeapMonth = true };
        Rejected(() => ScheduleJson.Serialize([invalid]), "solar leap-month flag rejected");
        invalid = Event(); invalid.LastRemindedOccurrenceDate = new DateOnly(2026, 10, 25);
        Rejected(() => ScheduleJson.Serialize([invalid]), "non-occurrence reminder date rejected");
        // A valid lunar February 30 must not be constructed as Gregorian February 30.
        int lunarYear = Enumerable.Range(1901, 200).First(y => Lunar.DaysInLunarMonth(y, 2) == 30);
        var lunar = Event(lunarYear, 2, 30, calendar: CalendarKind.Lunar);
        Check(ScheduleJson.Parse(ScheduleJson.Serialize([lunar])).Single().StartDate.Day == 30, "lunar February 30 preserved as numeric components");
        Check(ScheduleJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "examples", "schedules.json"))).Count == 2 &&
            ScheduleJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "examples", "empty-schedules.json"))).Count == 0,
            "published JSON examples pass real parser");
    }

    private static void OccurrenceChecks()
    {
        var item = Event();
        Check(ScheduleOccurrence.IsOnDate(item, new DateTime(2026, 10, 26)) &&
            !ScheduleOccurrence.IsOnDate(item, new DateTime(2026, 10, 27)), "nonrecurring event occurs once");
        foreach (var repeat in Enum.GetValues<RepeatPeriod>())
        {
            item.Recurrence = repeat;
            Check(!ScheduleOccurrence.IsOnDate(item, new DateTime(2025, 10, 26)), $"{repeat} does not occur before start");
        }
        item.Recurrence = RepeatPeriod.Daily;
        item.RepeatCount = 999;
        Check(ScheduleOccurrence.IsOnDate(item, new DateTime(2026, 10, 27)), "daily repeats next day");
        item.Recurrence = RepeatPeriod.Weekly;
        Check(ScheduleOccurrence.IsOnDate(item, new DateTime(2026, 11, 2)) &&
            !ScheduleOccurrence.IsOnDate(item, new DateTime(2026, 10, 27)), "weekly repeats at seven-day intervals");
        item = Event(2026, 1, 31, RepeatPeriod.Monthly);
        Check(!ScheduleOccurrence.IsOnDate(item, new DateTime(2026, 2, 28)) &&
            ScheduleOccurrence.IsOnDate(item, new DateTime(2026, 3, 31)) &&
            !ScheduleOccurrence.IsOnDate(item, new DateTime(2026, 4, 30)), "monthly 31 skips short months without drifting");
        item = Event(2024, 2, 29, RepeatPeriod.Yearly);
        Check(!ScheduleOccurrence.IsOnDate(item, new DateTime(2025, 2, 28)) &&
            ScheduleOccurrence.IsOnDate(item, new DateTime(2028, 2, 29)), "yearly February 29 skips non-leap years");
        int year = Enumerable.Range(2020, 10).First(y => Lunar.LeapMonthOf(y) != 0);
        int month = Lunar.LeapMonthOf(year);
        var normal = Lunar.ToSolar(year, month, 10, false);
        var leap = Lunar.ToSolar(year, month, 10, true);
        item = Event(year, month, 10, RepeatPeriod.Monthly, CalendarKind.Lunar);
        Check(ScheduleOccurrence.IsOnDate(item, normal) && ScheduleOccurrence.IsOnDate(item, leap), "lunar monthly includes ordinary and leap month");
        item.Recurrence = RepeatPeriod.Yearly;
        Check(ScheduleOccurrence.IsOnDate(item, normal) && !ScheduleOccurrence.IsOnDate(item, leap), "lunar yearly preserves ordinary month identity");
        item.StartDate = item.StartDate with { IsLeapMonth = true };
        int followingLeapYear = Enumerable.Range(year + 1, 2100 - year).First(y => Lunar.LeapMonthOf(y) == month);
        Check(!ScheduleOccurrence.IsOnDate(item, normal) &&
            ScheduleOccurrence.IsOnDate(item, Lunar.ToSolar(followingLeapYear, month, 10, true)), "lunar yearly leap event waits for matching leap month");
        item = Event(2026, 12, 8, RepeatPeriod.Yearly, CalendarKind.Lunar);
        Check(ScheduleOccurrence.IsOnDate(item, Lunar.ToSolar(2027, 12, 8, false)), "lunar twelfth month matches across Gregorian year boundary");
        item = Event(2026, 1, 1, RepeatPeriod.Weekly, CalendarKind.Lunar);
        var start = item.StartDate.ToSolar(item.Calendar);
        Check(ScheduleOccurrence.IsOnDate(item, start.AddDays(7)) && !ScheduleOccurrence.IsOnDate(item, start.AddDays(8)), "lunar weekly uses actual seven days");
        int longMonth = Enumerable.Range(1, 12).First(m => Lunar.DaysInLunarMonth(2026, m) == 30);
        int shortMonth = Enumerable.Range(longMonth + 1, 12 - longMonth).First(m => Lunar.DaysInLunarMonth(2026, m) == 29);
        item = Event(2026, longMonth, 30, RepeatPeriod.Monthly, CalendarKind.Lunar);
        Check(!ScheduleOccurrence.IsOnDate(item, Lunar.ToSolar(2026, shortMonth, 29, false)), "lunar monthly 30 skips 29-day months");
        var calendar = new CalendarView();
        item = Event(2026, 10, 1, RepeatPeriod.Daily);
        calendar.Rebuild(2026, 10, [item]);
        var cells = ((ItemsControl)calendar.FindName("DaysItems")).Items.Cast<CalendarDayViewModel>();
        Check(cells.Where(d => d.IsCurrentMonth).All(d => d.HasSchedule && d.Schedules.Count == 1), "calendar marks every repeated occurrence once");
    }

    private static void ReminderChecks()
    {
        var item = Event(recurrence: RepeatPeriod.Daily);
        var now = new DateTime(2026, 10, 26, 8, 45, 0);
        Check(ReminderService.FindDue([item], now, out var occurrence) == item && occurrence == new DateOnly(2026, 10, 26), "reminder identifies occurrence date");
        item.LastRemindedOccurrenceDate = occurrence;
        Check(ReminderService.FindDue([item], now.AddSeconds(1), out _) is null, "same occurrence not reminded twice");
        Check(ReminderService.FindDue([item], now.AddDays(1), out occurrence) == item && occurrence == new DateOnly(2026, 10, 27), "daily reminders repeat within same year");
        item.EventTime = new TimeOnly(0, 5);
        Check(ReminderService.FindDue([item], new DateTime(2026, 12, 31, 23, 50, 0), out occurrence) == item &&
            occurrence == new DateOnly(2027, 1, 1), "early reminder across midnight/year records target day");
        item = Event(2026, 10, 26);
        item.EventTime = new TimeOnly(23, 59); item.RemindingMinutes = 0;
        Check(ReminderService.FindDue([item], new DateTime(2026, 10, 27, 0, 1, 0), out occurrence) == item &&
            occurrence == new DateOnly(2026, 10, 26), "three-minute grace crosses midnight");
        Check(ReminderService.FindDue([item], new DateTime(2026, 10, 27, 0, 3, 0), out _) is null, "expired reminders not replayed");
        var other = Event(2026, 10, 26); other.EventTime = item.EventTime; other.RemindingMinutes = 0;
        item.LastRemindedOccurrenceDate = new DateOnly(2026, 10, 26);
        Check(ReminderService.FindDue([item, other], new DateTime(2026, 10, 26, 23, 59, 0), out _) == other, "one event reminder does not suppress another");
    }

    private static void PersistenceChecks()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "event-fixtures", Guid.NewGuid().ToString());
        Directory.CreateDirectory(folder);
        try
        {
            string file = Path.Combine(folder, "schedules.json");
            var service = new ScheduleService(file);
            Check(service.Reload() && service.Items.Count == 0 && !File.Exists(file), "missing file loads empty without writing");
            var item = Event(recurrence: RepeatPeriod.Daily);
            Check(service.Add(item) && File.Exists(file), "add saves standard JSON");
            string id = item.Id;
            item.LastRemindedOccurrenceDate = new DateOnly(2026, 10, 26);
            Check(service.Save() && File.Exists(file + ".bak"), "save retains backup and reminder state");
            var reloaded = new ScheduleService(file);
            Check(reloaded.Reload() && reloaded.Items.Single().Id == id &&
                reloaded.Items.Single().LastRemindedOccurrenceDate == new DateOnly(2026, 10, 26), "restart preserves ID and reminder deduplication");
            Check(ReminderService.FindDue(reloaded.Items, new DateTime(2026, 10, 26, 8, 45, 0), out _) is null, "restart does not repeat recorded reminder");
            var replacement = Event(recurrence: RepeatPeriod.Daily); replacement.EventName = "renamed";
            Check(service.Update(item, replacement) && replacement.Id == id && replacement.LastRemindedOccurrenceDate == item.LastRemindedOccurrenceDate, "metadata edit preserves identity and reminder");
            var changed = Event(recurrence: RepeatPeriod.Weekly);
            Check(service.Update(replacement, changed) && changed.LastRemindedOccurrenceDate is null, "rule edit resets reminder identity");
            var invalid = Event(); invalid.EventName = "";
            string before = File.ReadAllText(file);
            Check(!service.Add(invalid) && service.Items.Count == 1 && File.ReadAllText(file) == before, "invalid save rolls back without overwriting");
            string temp = file + ".tmp";
            File.WriteAllText(temp, "blocked temporary file");
            File.SetAttributes(temp, FileAttributes.ReadOnly);
            try
            {
                Check(!service.Add(Event()) && service.Items.Count == 1 && File.ReadAllText(file) == before,
                    "read-only temporary file reports failure and rolls back without throwing");
            }
            finally { File.SetAttributes(temp, FileAttributes.Normal); File.Delete(temp); }
            File.WriteAllText(file, "[]");
            Check(!service.Save() && File.ReadAllText(file) == "[]", "external edits block overwrite");
            Check(!service.Reload() && !service.CanSave && !service.Add(Event()) && File.ReadAllText(file) == "[]", "old format read failure blocks writes");
            Check(!service.Delete(changed) && service.Items.Count == 1, "failed delete rolls back in memory");
            File.WriteAllText(file, ScheduleJson.Serialize([changed]));
            Check(service.Reload() && service.CanSave && service.Delete(service.Items.Single()) && service.Items.Count == 0, "valid reload recovers writes and delete");
            var due = Event();
            var now = DateTime.Now;
            due.StartDate = new ScheduleDate { Year = now.Year, Month = now.Month, Day = now.Day };
            due.EventTime = new TimeOnly(now.Hour, now.Minute); due.RemindingMinutes = 0;
            Check(service.Add(due), "due fixture saved");
            var owner = new Window();
            int fires = 0, errors = 0;
            using (var reminder = new ReminderService(service, owner, _ => { }, _ => fires++, _ => errors++))
            {
                typeof(ReminderService).GetProperty("IsAlertActive")!.SetValue(reminder, true);
                File.WriteAllText(file, "[]");
                Invoke(reminder, "OnTick", null, EventArgs.Empty);
                Check(fires == 0 && errors == 1 && due.LastRemindedOccurrenceDate is null,
                    "active alert does not suppress other reminders; failed save reports and rolls back");
            }
            owner.Close();
        }
        finally { Directory.Delete(folder, true); }
    }

    private static void EditorChecks()
    {
        var window = Editor(new DateTime(2026, 10, 26), null);
        try
        {
            Check(window.Width == 400 && window.Height == 336 && window.FontSize == 14, "editor default width matches main window");
            Check((string)Find<Button>(window, "CalendarTypeButton").Content == "阳历", "new event defaults solar");
            Check(Find<ComboBox>(window, "RecurrenceCombo").Items.Count == 5, "five recurrence options");
            Check(Find<ComboBox>(window, "YearCombo").SelectedValue is 2026, "solar year selected");
            Find<TextBox>(window, "EventNameTextBox").Text = "事项";
            Find<Button>(window, "CalendarTypeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check((string)Find<Button>(window, "CalendarTypeButton").Content == "阴历", "calendar button toggles lunar");
            Check(OptionText(Find<ComboBox>(window, "YearCombo").SelectedItem) == "二〇二六", "lunar year uses Chinese digits");
            Check(OptionText(Find<ComboBox>(window, "MonthCombo").SelectedItem) == "九" &&
                OptionText(Find<ComboBox>(window, "DayCombo").SelectedItem) == "十七", "lunar traditional month/day without month suffix");
            var result = Build(window);
            Check(result.StartDate.ToSolar(result.Calendar) == new DateTime(2026, 10, 26), "calendar toggle preserves actual date");
            Render(window, "editor-lunar");
            var labels = Descendants<TextBlock>((DependencyObject)window.Content).Select(t => t.Text).ToList();
            Check(labels.Count(t => t == "年") == 1 && labels.Count(t => t == "月") == 1 && labels.Count(t => t == "日") == 1, "single date row retains separate year month day labels");
            Check(Find<ComboBox>(window, "MonthCombo").Items.Cast<object>().Select(OptionText).Contains("冬") &&
                Find<ComboBox>(window, "MonthCombo").Items.Cast<object>().Select(OptionText).Contains("腊"), "winter and twelfth month labels");
            Find<ComboBox>(window, "DayCombo").SelectedValue = 21;
            Check(OptionText(Find<ComboBox>(window, "DayCombo").SelectedItem) == "廿一", "traditional twenty-first day retained");
            Find<Button>(window, "CalendarTypeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var solar = Build(window);
            Check(solar.StartDate.ToSolar(solar.Calendar) == result.StartDate.ToSolar(result.Calendar).AddDays(4), "lunar edit converts back to correct solar day");
            Render(window, "editor-solar");
            foreach (var recurrence in Enum.GetValues<RepeatPeriod>())
            {
                Find<ComboBox>(window, "RecurrenceCombo").SelectedValue = recurrence.ToString();
                Check(Build(window).Recurrence == recurrence, $"editor saves {recurrence} recurrence");
            }
            Find<TextBox>(window, "RemindMinutesTextBox").Text = "100";
            Check(!TryBuild(window, out _), "editor rejects invalid reminder");
            Find<TextBox>(window, "RemindMinutesTextBox").Text = "15";
            Find<TextBox>(window, "EventNameTextBox").Text = " ";
            Check(!TryBuild(window, out _), "editor rejects empty name");
        }
        finally { window.Close(); }
        var item = Event(2025, 6, 10, RepeatPeriod.Yearly, CalendarKind.Lunar, true);
        item.LastRemindedOccurrenceDate = DateOnly.FromDateTime(item.StartDate.ToSolar(item.Calendar));
        var edit = Editor(DateTime.Today, item);
        try
        {
            Check(OptionText(Find<ComboBox>(edit, "MonthCombo").SelectedItem) == "闰六", "existing leap month retained in editor");
            Check(Build(edit).Id == item.Id && Build(edit).LastRemindedOccurrenceDate == item.LastRemindedOccurrenceDate, "editor retains ID and unchanged reminder");
            Find<TextBox>(edit, "RemindMinutesTextBox").Text = "10";
            Check(Build(edit).LastRemindedOccurrenceDate is null, "changing reminder offset resets record");
            Find<ComboBox>(edit, "YearCombo").SelectedValue = 2026;
            Check(OptionText(Find<ComboBox>(edit, "MonthCombo").SelectedItem) == "六" && !Build(edit).StartDate.IsLeapMonth, "changing to non-leap year selects ordinary month");
            Render(edit, "editor-edit");
        }
        finally { edit.Close(); }
    }

    private static ScheduleEditWindow Editor(DateTime date, ScheduleItem? item)
    {
        var window = (ScheduleEditWindow)Activator.CreateInstance(typeof(ScheduleEditWindow),
            BindingFlags.NonPublic | BindingFlags.Instance, null, [date, item], null)!;
        Invoke(window, "ScheduleEditWindow_Loaded", window, new RoutedEventArgs());
        return window;
    }
    private static T Find<T>(FrameworkElement parent, string name) where T : class => (T)parent.FindName(name);
    private static string OptionText(object option) => (string)option.GetType().GetProperty("Text")!.GetValue(option)!;
    private static object? Invoke(object target, string method, params object?[] args) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static bool TryBuild(ScheduleEditWindow window, out ScheduleItem result)
    {
        object?[] args = [null, null];
        bool success = (bool)Invoke(window, "TryBuildResult", args)!;
        result = (ScheduleItem)args[0]!;
        return success;
    }
    private static ScheduleItem Build(ScheduleEditWindow window)
    {
        if (!TryBuild(window, out var item)) throw new Exception("Editor result unexpectedly invalid");
        return item;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var next in Descendants<T>(child)) yield return next;
        }
    }
    private static void Render(ScheduleEditWindow window, string name)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(364, 297)); root.Arrange(new Rect(0, 0, 364, 297)); root.UpdateLayout();
        Check(Find<ComboBox>(window, "DayCombo").TransformToAncestor(root).Transform(new Point(Find<ComboBox>(window, "DayCombo").ActualWidth, 30)).X <= root.ActualWidth, name + " date controls fit width");
        var bitmap = new RenderTargetBitmap(364, 297, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, 364, 297));
        bitmap.Render(background);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "previews"));
        using var file = File.Create(Path.Combine(AppContext.BaseDirectory, "previews", name + ".png"));
        encoder.Save(file);
    }
}
