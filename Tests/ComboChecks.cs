using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OneSchedule;

internal static class ComboChecks
{
    private static readonly string[] Names = ["RecurrenceCombo", "YearCombo", "MonthCombo", "DayCombo", "HourCombo", "MinuteCombo"];
    public static void Run()
    {
        var failures = new List<Exception>();
        foreach (var check in new Action[] { ScrollBottom, Appearance, InputBehavior })
        {
            try { check(); Console.WriteLine("PASS: dropdown " + check.Method.Name); }
            catch (Exception ex) { failures.Add(ex); Console.WriteLine("FAIL: dropdown " + check.Method.Name + ": " + ex.Message); }
        }
        if (failures.Count > 0) throw new AggregateException(failures);
    }

    private static void ScrollBottom()
    {
        var editor = Editor();
        try
        {
            editor.Opacity = 0; editor.ShowInTaskbar = false; editor.Width = 380; editor.Show(); Pump();
            int[] expectedCounts = [5, 200, 12, 31, 24, 60];
            for (int i = 0; i < Names.Length; i++)
            {
                var combo = (ComboBox)editor.FindName(Names[i]);
                Require(combo.Items.Count == expectedCounts[i] && combo.Items.Cast<object>().All(x => !string.IsNullOrWhiteSpace(Text(x))), Names[i] + " contains exactly its real nonempty options");
                combo.IsDropDownOpen = true; Pump();
                var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
                var scroll = Children<ScrollViewer>(popup.Child).Single();
                var bar = Children<ScrollBar>(scroll).Single(b => b.Orientation == Orientation.Vertical);
                if (combo.Items.Count <= 8)
                    Require(bar.Visibility != Visibility.Visible, "short recurrence list has no visible scrollbar");
                else
                {
                    Require(bar.ActualWidth <= 6.1, "dropdown scrollbar is at most 6 DIP wide");
                    Require(!Children<RepeatButton>(bar).Any(b => b.Command == ScrollBar.LineUpCommand || b.Command == ScrollBar.LineDownCommand),
                        "scrollbar has no end-arrow buttons");
                    var pageDown = Children<RepeatButton>(bar).Single(b => b.Command == ScrollBar.PageDownCommand);
                    scroll.ScrollToTop(); Pump();
                    ((System.Windows.Input.RoutedCommand)pageDown.Command).Execute(null, pageDown);
                    Pump();
                    Require(scroll.VerticalOffset > 0, "slim scrollbar track still pages down");
                    var thumb = Children<Thumb>(bar).Single();
                    scroll.ScrollToTop(); Pump();
                    thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                    thumb.RaiseEvent(new DragDeltaEventArgs(0, 10) { RoutedEvent = Thumb.DragDeltaEvent });
                    thumb.RaiseEvent(new DragCompletedEventArgs(0, 10, false) { RoutedEvent = Thumb.DragCompletedEvent });
                    Pump();
                    Require(scroll.VerticalOffset > 0, "slim scrollbar thumb still scrolls content");
                }
                scroll.ScrollToEnd(); Pump();
                var last = (ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(combo.Items.Count - 1);
                var viewport = Children<ScrollContentPresenter>(scroll).Single();
                var lastBounds = last.TransformToAncestor(viewport).TransformBounds(new Rect(last.RenderSize));
                double gap = viewport.ActualHeight - lastBounds.Bottom;
                Console.WriteLine($"DIAG: {Names[i]} count={combo.Items.Count}, extent={scroll.ExtentHeight:F2}, viewport={scroll.ViewportHeight:F2}, bottomGap={gap:F2}, last={Text(combo.Items[^1])}");
                Require(gap <= 1, Names[i] + " has no empty area after its last option");
                Require(viewport.ActualHeight <= 240.5, "at most eight 30 DIP rows visible");
                combo.IsDropDownOpen = false; Pump();
            }
        }
        finally { editor.Close(); }
    }

    private static void Appearance()
    {
        var editor = Editor();
        try
        {
            editor.Opacity = 0; editor.ShowInTaskbar = false; editor.Width = 380; editor.Show(); Pump();
            Require(((ComboBox)editor.FindName("YearCombo")).ActualWidth == 72, "year width is 72 DIP");
            foreach (var name in Names)
            {
                var combo = (ComboBox)editor.FindName(name);
                Require(combo.HorizontalContentAlignment == HorizontalAlignment.Center, "dropdown text horizontally centered");
                var label = Children<ContentPresenter>(combo).Single(c => c.Name == "ContentSite");
                var bounds = label.TransformToAncestor(combo).TransformBounds(new Rect(label.RenderSize));
                Require(Math.Abs(bounds.Left + bounds.Width / 2 - combo.ActualWidth / 2) < 1, "selected text centered across full field without arrow gutter");
                combo.IsDropDownOpen = true; Pump();
                var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
                var last = (ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(combo.Items.Count - 1);
                Require(last.HorizontalContentAlignment == HorizontalAlignment.Center, "popup item text centered");
                combo.IsDropDownOpen = false; Pump();
            }
            Require(((TextBox)editor.FindName("RemindMinutesTextBox")).TextAlignment == TextAlignment.Center, "reminder digits centered");
            ((Button)editor.FindName("CalendarTypeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            var year = (ComboBox)editor.FindName("YearCombo");
            var yearText = Children<TextBlock>(year).Single(t => t.Text == "二〇二六");
            var measured = new FormattedText(yearText.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(yearText.FontFamily, yearText.FontStyle, yearText.FontWeight, yearText.FontStretch), yearText.FontSize, yearText.Foreground, VisualTreeHelper.GetDpi(yearText).PixelsPerDip);
            Require(yearText.ActualWidth + .5 >= measured.Width, "four-character lunar year fits narrow field");
            InteractionChecks.CheckEditorLayout(editor);
            Render((FrameworkElement)editor.Content, "editor-rounded-combos");
            year.IsDropDownOpen = true; Pump();
            var yearPopup = (Popup)year.Template.FindName("PART_Popup", year);
            Render((FrameworkElement)yearPopup.Child, "year-dropdown");
            year.IsDropDownOpen = false;
        }
        finally { editor.Close(); }
    }

    private static void InputBehavior()
    {
        var editor = Editor();
        try
        {
            editor.Opacity = 0; editor.ShowInTaskbar = false; editor.Width = 380; editor.Show(); Pump();
            foreach (var name in Names)
            {
                var combo = (ComboBox)editor.FindName(name);
                var toggle = (ToggleButton)combo.Template.FindName("DropDownToggle", combo);
                foreach (double x in new[] { 3d, combo.ActualWidth - 3 })
                {
                    DependencyObject? hit = combo.InputHitTest(new Point(x, combo.ActualHeight / 2)) as DependencyObject;
                    while (hit is not null && hit != toggle) hit = VisualTreeHelper.GetParent(hit);
                    Require(hit == toggle, "entire field opens dropdown including both edges");
                }
                var peer = new ToggleButtonAutomationPeer(toggle);
                ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle(); Pump();
                Require(combo.IsDropDownOpen, "full-field toggle opens popup");
                ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle(); Pump();
                Require(!combo.IsDropDownOpen, "full-field toggle closes popup");
                combo.Focus();
                SendKey(combo, Key.F4); Pump();
                Require(combo.IsDropDownOpen, "F4 opens dropdown without arrow");
                SendKey(combo, Key.End); Pump();
                SendKey(combo, Key.Enter); Pump();
                Require(!combo.IsDropDownOpen && combo.SelectedIndex == combo.Items.Count - 1, "End then Enter selects final real option and closes");
                SendKey(combo, Key.F4); Pump();
                SendKey(combo, Key.Escape); Pump();
                Require(!combo.IsDropDownOpen, "Escape dismisses popup");
            }
            var minute = (ComboBox)editor.FindName("MinuteCombo");
            editor.Focus(); Pump();
            var border = (Border)minute.Template.FindName("FieldBorder", minute);
            Require(border.CornerRadius.TopLeft == 6, "field has rounded corners");
            var before = ((SolidColorBrush)border.Background).Color;
            var hoverKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            minute.SetValue(hoverKey, true); Pump();
            Require(((SolidColorBrush)border.Background).Color != before, "hover changes field lighting");
            minute.SetValue(hoverKey, false);
        }
        finally { editor.Close(); }
    }

    private static void SendKey(ComboBox combo, Key key) => combo.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(combo), 0, key) { RoutedEvent = Keyboard.KeyDownEvent });

    private static ScheduleEditWindow Editor() => (ScheduleEditWindow)Activator.CreateInstance(typeof(ScheduleEditWindow),
        BindingFlags.Instance | BindingFlags.NonPublic, null, [new DateTime(2026, 10, 26), null], null)!;
    private static string Text(object value) => value switch { string s => s, ComboBoxItem item => item.Content?.ToString() ?? "", _ => value.GetType().GetProperty("Text")?.GetValue(value)?.ToString() ?? "" };
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
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
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "previews"));
        using var file = File.Create(Path.Combine(AppContext.BaseDirectory, "previews", name + ".png"));
        encoder.Save(file);
    }
}
