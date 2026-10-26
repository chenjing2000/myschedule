using System.Reflection;
using System.IO;
using System.Windows.Input;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using OneSchedule;

internal static class RepeatChecks
{
    public static void Run()
    {
        var item = Item(RepeatPeriod.Daily, 2);
        Require(ScheduleOccurrence.IsOnDate(item, new(2026, 1, 31)) && ScheduleOccurrence.IsOnDate(item, new(2026, 2, 1)) &&
            !ScheduleOccurrence.IsOnDate(item, new(2026, 2, 2)), "daily total includes start and stops after two");
        item = Item(RepeatPeriod.Weekly, 2);
        Require(ScheduleOccurrence.IsOnDate(item, new(2026, 2, 7)) && !ScheduleOccurrence.IsOnDate(item, new(2026, 2, 14)), "weekly stops after second occurrence");
        item = Item(RepeatPeriod.Monthly, 3);
        Require(ScheduleOccurrence.IsOnDate(item, new(2026, 5, 31)) && !ScheduleOccurrence.IsOnDate(item, new(2026, 7, 31)), "short months do not consume a monthly count");
        item = Item(RepeatPeriod.Yearly, 2);
        item.StartDate = new() { Year = 2024, Month = 2, Day = 29 };
        Require(ScheduleOccurrence.IsOnDate(item, new(2028, 2, 29)) && !ScheduleOccurrence.IsOnDate(item, new(2032, 2, 29)), "nonleap years do not consume a yearly count");
        item = Item(RepeatPeriod.Monthly, 2);
        item.Calendar = CalendarKind.Lunar; item.StartDate = new() { Year = 2023, Month = 2, Day = 1 };
        Require(ScheduleOccurrence.IsOnDate(item, Lunar.ToSolar(2023, 2, 1, true)) &&
            !ScheduleOccurrence.IsOnDate(item, Lunar.ToSolar(2023, 3, 1, false)), "lunar monthly counts ordinary and leap months separately");
        item.StartDate = new() { Year = 2023, Month = 2, Day = 1, IsLeapMonth = true };
        Require(!ScheduleOccurrence.IsOnDate(item, Lunar.ToSolar(2023, 2, 1, false)) &&
            ScheduleOccurrence.IsOnDate(item, Lunar.ToSolar(2023, 3, 1, false)) &&
            !ScheduleOccurrence.IsOnDate(item, Lunar.ToSolar(2023, 4, 1, false)), "lunar leap start excludes the preceding ordinary month");
        item = Item(RepeatPeriod.Daily, 1);
        item.LastRemindedOccurrenceDate = new(2026, 1, 31);
        var replacement = Item(RepeatPeriod.Daily, 2);
        Require(ScheduleOccurrence.SameReminderIdentity(item, replacement), "count expansion preserves valid reminder deduplication");
        replacement.StartDate = item.StartDate;
        typeof(ScheduleItem).GetProperty("RepeatCount")!.SetValue(item, 3);
        item.LastRemindedOccurrenceDate = new(2026, 2, 2);
        Require(!ScheduleOccurrence.SameReminderIdentity(item, replacement), "count shrink clears an out-of-range reminder record");
        item = Item(RepeatPeriod.Daily, 2);
        string json = ScheduleJson.Serialize([item]);
        Require(JsonNode.Parse(json)!["schemaVersion"]!.GetValue<int>() == 2 &&
            ScheduleJson.Parse(json).Count == 1, "strict version2 JSON roundtrip");
        foreach (int invalid in new[] { 0, -1, 1000 })
        {
            typeof(ScheduleItem).GetProperty("RepeatCount")!.SetValue(item, invalid);
            Reject(() => ScheduleJson.Serialize([item]), "invalid repeat count rejected");
        }
        var node = JsonNode.Parse(json)!;
        node["events"]![0]!.AsObject().Remove("repeatCount");
        Reject(() => ScheduleJson.Parse(node.ToJsonString()), "missing count rejected");
        node["events"]![0]!["repeatCount"] = 1.5;
        try { ScheduleJson.Parse(node.ToJsonString()); throw new Exception("fractional JSON count accepted"); }
        catch (System.Text.Json.JsonException) { }
        node["schemaVersion"] = 1;
        Reject(() => ScheduleJson.Parse(node.ToJsonString()), "version1 rejected without automatic conversion");
        Console.WriteLine("PASS: finite-repeat core checks");
        EditorChecks();
        UpgradeChecks();
        ReferenceChecks();
        PersistenceChecks();
    }

    private static void EditorChecks()
    {
        var editor = (ScheduleEditWindow)Activator.CreateInstance(typeof(ScheduleEditWindow), BindingFlags.Instance | BindingFlags.NonPublic, null,
            [new DateTime(2026, 10, 26), null], null)!;
        try
        {
            typeof(ScheduleEditWindow).GetMethod("ScheduleEditWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, [editor, new RoutedEventArgs()]);
            var count = editor.FindName("RepeatCountTextBox") as TextBox ?? throw new Exception("repeat input is missing");
            var period = (ComboBox)editor.FindName("RecurrenceCombo");
            Require(!count.IsEnabled && count.Text == "1", "nonperiodic repeat field disabled at one");
            foreach (var value in new[] { ("Daily", "2"), ("Weekly", "2"), ("Monthly", "2"), ("Yearly", "100") })
            {
                period.SelectedValue = value.Item1;
                Require(count.IsEnabled && count.Text == value.Item2, "period-specific repeat defaults");
            }
            Require(count.MaxLength == 3 && count.Width == 42 && ((ComboBox)editor.FindName("MonthCombo")).Width == 42,
                "repeat and small controls share compact integer widths");
            foreach (string text in new[] { ".", "-", "+", "三" })
            {
                var input = new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, count, text))
                    { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
                count.RaiseEvent(input);
                Require(input.Handled, "repeat input rejects non-ASCII-digit typing");
            }
            foreach (string text in new[] { "1234", "-2", "2.0", "+3" })
            {
                count.SelectAll();
                var paste = new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText, text), false, DataFormats.UnicodeText);
                count.RaiseEvent(paste);
                Require(paste.CommandCancelled, "repeat input rejects invalid paste");
            }
            count.Text = "0";
            ((TextBox)editor.FindName("EventNameTextBox")).Text = "valid";
            object?[] args = [null, null];
            Require(!(bool)typeof(ScheduleEditWindow).GetMethod("TryBuildResult", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, args)!, "zero repeat count rejected on save");
            editor.Opacity = 0; editor.ShowInTaskbar = false; editor.Show();
            editor.UpdateLayout();
            double compactHeight = editor.ActualHeight;
            period.SelectedValue = "Daily"; count.Text = "0";
            ((TextBox)editor.FindName("EventNameTextBox")).Text = "valid";
            var save = ((System.Windows.Controls.Panel)editor.Content).Children.OfType<System.Windows.Controls.Grid>().Last().Children.OfType<Button>().Single(b => b.Content?.ToString() == "保存");
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var frame = new System.Windows.Threading.DispatcherFrame();
            editor.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            editor.UpdateLayout();
            Require(editor.ActualHeight > compactHeight && ((TextBlock)editor.FindName("ValidationText")).ActualHeight > 0,
                "validation message grows the compact window without clipping");
        }
        finally { editor.Close(); }
        var existing = Item(RepeatPeriod.Yearly, 321);
        var edit = (ScheduleEditWindow)Activator.CreateInstance(typeof(ScheduleEditWindow), BindingFlags.Instance | BindingFlags.NonPublic, null,
            [new DateTime(2026, 10, 26), existing], null)!;
        try
        {
            typeof(ScheduleEditWindow).GetMethod("ScheduleEditWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(edit, [edit, new RoutedEventArgs()]);
            Require(((TextBox)edit.FindName("RepeatCountTextBox")).Text == "321", "editing preserves saved repeat count instead of defaulting");
        }
        finally { edit.Close(); }
        Console.WriteLine("PASS: repeat editor defaults and validation");
    }

    private static void UpgradeChecks()
    {
        var source = JsonNode.Parse(ScheduleJson.Serialize(Enum.GetValues<RepeatPeriod>().Select(p => Item(p, 1))))!;
        source["schemaVersion"] = 1;
        foreach (var node in source["events"]!.AsArray()) node!.AsObject().Remove("repeatCount");
        source["events"]![0]!["recurrence"] = "NONE";
        source["events"]![4]!["recurrence"] = "Yearly";
        source["events"]![1]!["lastRemindedOccurrenceDate"] = "2026-02-04";
        source["events"]![2]!["lastRemindedOccurrenceDate"] = "2026-02-07";
        string original = source.ToJsonString();
        var result = ScheduleJson.Parse(DataUpgrade.ConvertVersion1(original));
        Require(result.Select(i => i.RepeatCount).SequenceEqual(new[] { 1, 2, 2, 2, 100 }), "upgrade assigns approved defaults");
        Require(result[1].LastRemindedOccurrenceDate is null && result[2].LastRemindedOccurrenceDate == new DateOnly(2026, 2, 7),
            "upgrade clears only reminder dates outside the new range");
        for (int i = 0; i < result.Count; i++)
            Require(result[i].Id == source["events"]![i]!["id"]!.GetValue<string>() && result[i].EventName == "finite",
                "upgrade preserves event identity and content");
        string folder = Path.Combine(AppContext.BaseDirectory, "upgrade-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "events.json");
            File.WriteAllText(path, original, new System.Text.UTF8Encoding(true));
            byte[] bytes = File.ReadAllBytes(path);
            File.WriteAllText(path + ".bak", "existing rolling backup");
            Require(DataUpgrade.UpgradeFile(path) == 0, "explicit file upgrade succeeds");
            Require(File.ReadAllBytes(Directory.GetFiles(folder, "*.schema-v1-*.bak").Single()).SequenceEqual(bytes), "original backup preserves bytes including BOM");
            Require(File.ReadAllText(path + ".bak") == "existing rolling backup", "existing rolling backup preserved");
            string upgraded = File.ReadAllText(path);
            Require(DataUpgrade.UpgradeFile(path) == 0 && File.ReadAllText(path) == upgraded && Directory.GetFiles(folder, "*.schema-v1-*.bak").Length == 1,
                "re-running upgrade on version2 changes nothing");
        }
        finally { Directory.Delete(folder, true); }
        Console.WriteLine("PASS: explicit version1 upgrade and backup checks");
    }

    private static void ReferenceChecks()
    {
        foreach (var setup in new[] {
            (CalendarKind.Solar, RepeatPeriod.Monthly, new ScheduleDate { Year=2026, Month=1, Day=31 }, new DateTime(2028,12,31)),
            (CalendarKind.Solar, RepeatPeriod.Yearly, new ScheduleDate { Year=2024, Month=2, Day=29 }, new DateTime(2036,12,31)),
            (CalendarKind.Lunar, RepeatPeriod.Monthly, new ScheduleDate { Year=2023, Month=2, Day=30 }, new DateTime(2026,12,31)),
            (CalendarKind.Lunar, RepeatPeriod.Yearly, new ScheduleDate { Year=2023, Month=2, Day=1, IsLeapMonth=true }, new DateTime(2050,12,31)) })
        {
            var item = Item(setup.Item2, 2); item.Calendar = setup.Item1; item.StartDate = setup.Item3;
            DateTime start = item.StartDate.ToSolar(item.Calendar);
            int occurrences = 0;
            for (DateTime day = start; day <= setup.Item4; day = day.AddDays(1))
            {
                bool match;
                if (item.Calendar == CalendarKind.Solar)
                    match = day.Day == item.StartDate.Day && (item.Recurrence == RepeatPeriod.Monthly || day.Month == item.StartDate.Month);
                else
                {
                    var lunar = Lunar.FromSolar(day);
                    match = lunar.Day == item.StartDate.Day && (item.Recurrence == RepeatPeriod.Monthly ||
                        lunar.Month == item.StartDate.Month && lunar.IsLeap == item.StartDate.IsLeapMonth);
                }
                bool expected = match && ++occurrences <= 2;
                Require(ScheduleOccurrence.IsOnDate(item, day) == expected, "finite recurrence agrees with independent day-by-day enumeration: " + day.ToString("yyyy-MM-dd"));
            }
        }
        Console.WriteLine("PASS: independent solar/lunar daily enumeration checks");
    }

    private static void PersistenceChecks()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "finite-fixtures", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "events.json");
        var service = new ScheduleService(path);
        try
        {
            Require(service.Reload(), "finite fixture loads empty");
            var item = Item(RepeatPeriod.Daily, 5);
            item.LastRemindedOccurrenceDate = new DateOnly(2026, 2, 3);
            Require(service.Add(item), "finite event with valid reminder saved");
            var shrunk = Item(RepeatPeriod.Daily, 2);
            Require(service.Update(item, shrunk) && shrunk.LastRemindedOccurrenceDate is null, "shrinking count persists and clears expired reminder record");
            Require(service.Items.Count == 1 && service.GetForDate(new DateTime(2026, 2, 2)).Count == 0,
                "expired schedule remains stored but has no later occurrences");
            Require(ReminderService.FindDue(service.Items, new DateTime(2026, 2, 2, 9, 0, 0), out _) is null,
                "no reminder after finite count is exhausted");
            shrunk.LastRemindedOccurrenceDate = new DateOnly(2026, 1, 31);
            Require(service.Save(), "valid deduplication record saved");
            var expanded = Item(RepeatPeriod.Daily, 5);
            Require(service.Update(shrunk, expanded) && expanded.LastRemindedOccurrenceDate == new DateOnly(2026, 1, 31),
                "expanding count preserves still-valid reminder record");
            var reload = new ScheduleService(path);
            Require(reload.Reload() && reload.Items.Single().RepeatCount == 5, "restart preserves finite repeat count");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        Console.WriteLine("PASS: finite persistence, expiry and reminder checks");
    }

    private static ScheduleItem Item(RepeatPeriod period, int count)
    {
        var item = new ScheduleItem { Id = Guid.NewGuid().ToString(), EventName = "finite", StartDate = new() { Year = 2026, Month = 1, Day = 31 }, EventTime = new(9, 0), Recurrence = period };
        var property = typeof(ScheduleItem).GetProperty("RepeatCount") ?? throw new Exception("RepeatCount is missing from the persisted model");
        property.SetValue(item, count);
        return item;
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException) { return; }
        throw new Exception(message);
    }
}
