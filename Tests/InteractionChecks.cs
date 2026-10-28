using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OneSchedule;

internal static class InteractionChecks
{
    public static void Run()
    {
        var failures = new List<Exception>();
        foreach (var check in new Action[] { MainWindowChrome, Calendar, Editor, DayList })
        {
            try { check(); Console.WriteLine("PASS: interaction " + check.Method.Name); }
            catch (Exception ex) { failures.Add(ex); Console.WriteLine("FAIL: interaction " + check.Method.Name + ": " + ex.Message); }
        }
        if (failures.Count > 0) throw new AggregateException(failures);
    }

    private static void MainWindowChrome()
    {
        var window = new MainWindow();
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            int style = GetWindowLong(handle, -16);
            Require((style & 0x00010000) == 0, "main window maximize button is disabled");
            Require((style & 0x00020000) != 0, "main window minimize button remains enabled");
            Require((style & 0x00040000) != 0, "main window retains resize border");
        }
        finally { window.Close(); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    private static void Calendar()
    {
        var selectedDate = DateTime.Today == new DateTime(2026, 10, 28) ? new DateTime(2026, 10, 27) : new DateTime(2026, 10, 28);
        var unselectedDate = DateTime.Today == new DateTime(2026, 10, 21) ? new DateTime(2026, 10, 22) : new DateTime(2026, 10, 21);
        var calendar = new CalendarView();
        calendar.Rebuild(2026, 10, []);
        Layout(calendar, 344, 320);
        var button = Children<Button>(calendar).Single(b => b.DataContext is CalendarDayViewModel d && d.Date == selectedDate);
        int invocations = 0;
        DateTime invoked = default;
        calendar.DayInvoked += (_, e) => { invocations++; invoked = e.Date; };
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(invocations == 0, "single click must select without opening a day dialog");
        var circle = Children<Border>(button).Single(b => b.Name == "DayCircle");
        Require(circle.Width == 44 && circle.Height == 44 && circle.BorderThickness.Left > 0,
            "selected day has a 44 DIP circular outline");
        var accent = ((SolidColorBrush)calendar.FindResource("AccentBrush")).Color;
        Require(Children<TextBlock>(circle).All(t => ((SolidColorBrush)t.Foreground).Color == accent), "both selected date texts use accent color");
        button.RaiseEvent(MouseEvent(Control.MouseDoubleClickEvent));
        Require(invocations == 1 && invoked == selectedDate, "double click opens exactly the selected date once");
        calendar.Rebuild(2026, 11, []);
        calendar.Rebuild(2026, 10, []);
        Layout(calendar, 344, 320);
        button = Children<Button>(calendar).Single(b => b.DataContext is CalendarDayViewModel d && d.Date == invoked);
        Require(Children<Border>(button).Single(b => b.Name == "DayCircle").BorderThickness.Left > 0, "selected date survives month rebuilds");
        var hoverKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        button.SetValue(hoverKey, true);
        circle = Children<Border>(button).Single(b => b.Name == "DayCircle");
        var cell = Children<Border>(button).Single(b => b.Name == "CellBorder");
        Require(((SolidColorBrush)circle.Background).Color == ((SolidColorBrush)calendar.FindResource("HoverBrush")).Color &&
            ((SolidColorBrush)cell.Background).Color != ((SolidColorBrush)circle.Background).Color, "hover fills only the circular area");
        Render(calendar, "selected-hover");
        calendar.SetReminderFlash(true);
        calendar.UpdateLayout();
        Require(((SolidColorBrush)circle.Background).Color.R < 150, "hovered day background stays dark during reminder flash: " + circle.Background);
        Require(Children<TextBlock>(button).All(t => ((SolidColorBrush)t.Foreground).Color.R > 200), "selection remains readable during reminder flash");
        var unselected = Children<Button>(calendar).Single(b => b.DataContext is CalendarDayViewModel d && d.Date == unselectedDate);
        unselected.SetValue(hoverKey, true);
        calendar.UpdateLayout();
        var unselectedCircle = Children<Border>(unselected).Single(b => b.Name == "DayCircle");
        Require(((SolidColorBrush)unselectedCircle.Background).Color.R < 150 &&
            Children<TextBlock>(unselectedCircle).All(t => ((SolidColorBrush)t.Foreground).Color.R > 200), "unselected hovered date also retains dark-flash contrast");
        unselected.SetValue(hoverKey, false);
        Render(calendar, "selected-hover-reminder");
        calendar.SetReminderFlash(false);
        button.SetValue(hoverKey, false);
        calendar.Rebuild(DateTime.Today.Year, DateTime.Today.Month, []);
        Layout(calendar, 344, 320);
        var today = Children<Button>(calendar).Single(b => b.DataContext is CalendarDayViewModel { IsToday: true });
        today.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        today.SetValue(hoverKey, true);
        circle = Children<Border>(today).Single(b => b.Name == "DayCircle");
        Require(circle.BorderThickness.Left == 0 && ((SolidColorBrush)circle.Background).Color == accent,
            "selected and hovered today retains its filled circle without an extra outline");
        today.SetValue(hoverKey, false);
        calendar.Rebuild(2026, 10, []);
        Layout(calendar, 344, 320);
        Children<Button>(calendar).Single(b => b.DataContext is CalendarDayViewModel d && d.Date == selectedDate)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Render(calendar, "selected-date");
    }

    private static void Editor()
    {
        Require(new ScheduleItem().RemindingMinutes == 0, "new model reminder defaults to zero");
        var editor = NewEditor(null);
        try
        {
            var reminder = Find<TextBox>(editor, "RemindMinutesTextBox");
            Require(reminder.Text == "0", "new editor reminder defaults to zero");
            reminder.Text = "15"; reminder.CaretIndex = 0;
            reminder.RaiseEvent(MouseEvent(UIElement.PreviewMouseLeftButtonDownEvent));
            Require(reminder.CaretIndex == 2 && reminder.SelectionLength == 0, "click places reminder caret after digits without selecting");
            foreach (double width in new[] { 384d, 364d, 504d })
            {
                Layout((FrameworkElement)editor.Content, width, 297);
                CheckEditorLayout(editor);
            }
            Find<Button>(editor, "CalendarTypeButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            foreach (int year in new[] { 2023, 2025, 2033 })
            {
                Find<ComboBox>(editor, "YearCombo").SelectedValue = year;
                foreach (var month in Find<ComboBox>(editor, "MonthCombo").Items.Cast<object>().ToArray())
                {
                    Find<ComboBox>(editor, "MonthCombo").SelectedItem = month;
                    var days = Find<ComboBox>(editor, "DayCombo");
                    days.SelectedIndex = days.Items.Count - 1;
                    Layout((FrameworkElement)editor.Content, 364, 297);
                    CheckEditorLayout(editor);
                    foreach (var name in new[] { "YearCombo", "MonthCombo", "DayCombo", "HourCombo", "MinuteCombo" })
                    {
                        var combo = Find<ComboBox>(editor, name);
                        string expected = combo.SelectedItem is string str ? str : (string)combo.SelectedItem.GetType().GetProperty("Text")!.GetValue(combo.SelectedItem)!;
                        var label = Children<TextBlock>(combo).Single(t => t.Text == expected);
                        var measured = new FormattedText(expected, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                            new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch), label.FontSize, label.Foreground,
                            VisualTreeHelper.GetDpi(label).PixelsPerDip);
                        Require(label.ActualWidth + .5 >= measured.Width, "traditional lunar and time text fits dropdown content area");
                    }
                }
            }
            reminder.Text = "0";
            Layout((FrameworkElement)editor.Content, 364, 297);
            Render((FrameworkElement)editor.Content, "editor-compact-lunar");
        }
        finally { editor.Close(); }
        var existing = NewEditor(new ScheduleItem { Calendar = CalendarKind.Solar,
            StartDate = new ScheduleDate { Year = 2026, Month = 10, Day = 26 }, EventTime = new TimeOnly(9, 0), RemindingMinutes = 37 });
        try
        {
            Require(Find<TextBox>(existing, "RemindMinutesTextBox").Text == "37", "editing retains stored reminder value");
            Layout((FrameworkElement)existing.Content, 364, 297);
            CheckEditorLayout(existing);
        }
        finally { existing.Close(); }
    }

    private static void DayList()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "interaction-fixtures", Guid.NewGuid().ToString("N"));
        var service = new ScheduleService(Path.Combine(folder, "events.json"));
        Require(service.Reload() && service.Add(new ScheduleItem { EventName = "existing", StartDate = new ScheduleDate { Year = 2026, Month = 10, Day = 26 }, EventTime = new TimeOnly(9, 0) }), "day list fixture saved");
        var day = (DayEventsWindow)Activator.CreateInstance(typeof(DayEventsWindow), BindingFlags.Instance | BindingFlags.NonPublic, null, [new DateTime(2026, 10, 26), service], null)!;
        bool enabled = true;
        string? saveName = null;
        var opened = new List<(string Title, string Name, int Day)>();
        EventManager.RegisterClassHandler(typeof(ScheduleEditWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, e) =>
        {
            if (!enabled || sender is not ScheduleEditWindow editor || e.OriginalSource != editor) return;
            editor.Opacity = 0;
            editor.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                opened.Add((editor.Title, Find<TextBox>(editor, "EventNameTextBox").Text, (int)Find<ComboBox>(editor, "DayCombo").SelectedValue));
                if (saveName is { } name)
                {
                    saveName = null;
                    Find<TextBox>(editor, "EventNameTextBox").Text = name;
                    Children<Button>((DependencyObject)editor.Content).Single(b => b.Content?.ToString() == "保存")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                else editor.Close();
            }));
        }));
        try
        {
            day.Opacity = 0; day.ShowInTaskbar = false; day.Show();
            var list = Find<ListBox>(day, "ScheduleListBox");
            day.UpdateLayout();
            Require(list.SelectedItem is not null, "fixture starts with a selected event");
            list.RaiseEvent(MouseEvent(Control.MouseDoubleClickEvent, list));
            Require(opened.Count == 1 && opened[0] == ("新增日程", "", 26), "blank double click creates a blank event for the viewed date despite existing selection");
            var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            var text = Children<TextBlock>(row).First();
            list.RaiseEvent(MouseEvent(Control.MouseDoubleClickEvent, text));
            Require(opened.Count == 2 && opened[1].Title == "编辑日程" && opened[1].Name == "existing", "double click on row content edits that event");
            list.RaiseEvent(MouseEvent(Control.MouseDoubleClickEvent, row));
            Require(opened.Count == 3 && opened[2].Title == "编辑日程", "row whitespace also edits its event");
            var scroll = Children<ScrollBar>(list).First();
            list.RaiseEvent(MouseEvent(Control.MouseDoubleClickEvent, scroll));
            Require(opened.Count == 3, "scrollbar double click does not open an editor");
            saveName = "added";
            list.RaiseEvent(MouseEvent(Control.MouseDoubleClickEvent, list));
            Require(service.Items.Count == 2 && service.Items.Any(i => i.EventName == "added"), "new editor saved item reaches day list persistence");
            string originalId = service.Items.Single(i => i.EventName == "existing").Id;
            day.UpdateLayout();
            row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(list.Items.Cast<ScheduleItem>().Single(i => i.Id == originalId));
            saveName = "updated";
            list.RaiseEvent(MouseEvent(Control.MouseDoubleClickEvent, row));
            Require(service.Items.Count == 2 && service.Items.Single(i => i.Id == originalId).EventName == "updated",
                "edit saves replacement while preserving ID without a delete protocol");
            Find<Button>(day, "DeleteButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(service.Items.Count == 1 && service.Items[0].EventName == "added", "day-list Delete remains the deletion entry point");
        }
        finally { enabled = false; day.Close(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    internal static void CheckEditorLayout(ScheduleEditWindow editor)
    {
        var root = (FrameworkElement)editor.Content;
        var actions = Children<Button>(root).Where(b => b.Style == editor.FindResource("ActionStyle")).Select(b => b.Content?.ToString()).ToArray();
        Require(actions.SequenceEqual(new[] { "取消", "保存" }), "new/edit editor exposes only Cancel and Save actions");
        var combos = new[] { "RecurrenceCombo", "MonthCombo", "DayCombo", "HourCombo", "MinuteCombo" }.Select(n => Find<ComboBox>(editor, n)).ToArray();
        Require(combos.All(c => c.ActualWidth == 42), "all non-year dropdown widths are 42 DIP");
        Require(Find<TextBox>(editor, "RepeatCountTextBox").ActualWidth == 42 && Find<TextBox>(editor, "RemindMinutesTextBox").ActualWidth == 42,
            "numeric inputs share 42 DIP width");
        Require(Find<Button>(editor, "CalendarTypeButton").ActualWidth == 48 &&
            Children<Button>(root).Where(b => b.Style == editor.FindResource("ActionStyle")).All(b => b.ActualWidth == 60), "compact action widths");
        Require(Children<FrameworkElement>(root).Where(e => e is Button or ComboBox or TextBox).All(e => Math.Abs(e.ActualWidth - Math.Round(e.ActualWidth)) < .001),
            "editor controls have integer DIP widths");
        Require(Children<TextBlock>(root).Any(t => t.Text == "备注") && !Children<TextBlock>(root).Any(t => t.Text == "地点"),
            "new and edit dialogs use the remarks label");
        var unitLabels = Children<TextBlock>(root).Where(t => new[] { "年", "月", "日", "时", "分" }.Contains(t.Text));
        Require(unitLabels.All(t => t.Width == 22), "date and time unit slots share 22 DIP width");
        Require(Children<TextBlock>(root).Where(t => t.Text is "周期" or "重复").All(t => t.Width == 36), "period and repeat label slots match");
        if (Find<TextBlock>(editor, "ValidationText").Visibility == Visibility.Collapsed)
        {
            var cancel = Children<Button>(root).Single(b => b.Content?.ToString() == "取消");
            var reminder = Find<TextBox>(editor, "RemindMinutesTextBox");
            double gap = cancel.TransformToAncestor(root).Transform(new Point()).Y - reminder.TransformToAncestor(root).Transform(new Point(0, reminder.ActualHeight)).Y;
            Require(Math.Abs(gap - 16) <= 1, "compact reminder-to-footer gap is 16 DIP");
        }
        foreach (var element in Children<FrameworkElement>(root).Where(e => e is ComboBox or TextBox ||
                     e is TextBlock t && new[] { "年", "月", "日", "时", "分" }.Contains(t.Text)))
        {
            var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
            Require(bounds.Right <= root.ActualWidth + .5 && bounds.Left >= 0, "editor controls and unit labels stay within actual content width");
        }
        foreach (var name in new[] { "EventNameTextBox", "LocationTextBox" })
        {
            var box = Find<TextBox>(editor, name);
            var host = Children<ScrollViewer>(box).Single(h => h.Name == "PART_ContentHost");
            double inset = host.TransformToAncestor(box).Transform(new Point()).X;
            Require(inset <= 7 && box.Padding.Left <= 6, "textbox has one small horizontal inset");
        }
    }

    private static ScheduleEditWindow NewEditor(ScheduleItem? item)
    {
        var editor = (ScheduleEditWindow)Activator.CreateInstance(typeof(ScheduleEditWindow), BindingFlags.Instance | BindingFlags.NonPublic, null, [new DateTime(2026, 10, 26), item], null)!;
        typeof(ScheduleEditWindow).GetMethod("ScheduleEditWindow_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, [editor, new RoutedEventArgs()]);
        return editor;
    }
    private static MouseButtonEventArgs MouseEvent(RoutedEvent routedEvent, object? source = null) =>
        new(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = routedEvent, Source = source };
    private static T Find<T>(FrameworkElement root, string name) where T : class => (T)root.FindName(name);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Layout(FrameworkElement element, double width, double height)
    { element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout(); }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }
    private static void Render(FrameworkElement root, string name)
    {
        int width = (int)Math.Ceiling(root.ActualWidth + root.Margin.Left + root.Margin.Right);
        int height = (int)Math.Ceiling(root.ActualHeight + root.Margin.Top + root.Margin.Bottom);
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        image.Render(background);
        image.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "previews"));
        using var output = File.Create(Path.Combine(AppContext.BaseDirectory, "previews", name + ".png"));
        encoder.Save(output);
    }
}
