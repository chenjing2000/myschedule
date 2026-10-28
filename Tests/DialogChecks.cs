using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OneSchedule;

internal static class DialogChecks
{
    public static void Run()
    {
        Exception? failure = null;
        bool enabled = true;
        int dayCount = 0, editorCount = 0;
        var service = new ScheduleService();
        // Transparent real windows exercise WPF's native CenterOwner positioning without a visible dialog.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, e) =>
        {
            if (!enabled || sender is not Window dialog || e.OriginalSource != dialog ||
                dialog is not (DayEventsWindow or ScheduleEditWindow)) return;
            dialog.Opacity = 0;
            dialog.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                try
                {
                    var owner = dialog.Owner;
                    Require(Math.Abs(dialog.ActualWidth - owner.ActualWidth) < 1, "dialog matches actual owner width");
                    Require(Math.Abs(dialog.Left - owner.Left) < 1, "dialog left aligns with owner");
                    Require(Math.Abs(dialog.Top + dialog.ActualHeight / 2 - owner.Top - owner.ActualHeight / 2) < 1, "dialog vertically centers on owner");
                    Require(dialog.FontFamily.Source == owner.FontFamily.Source && dialog.FontSize == 14, "dialog font family and size consistent");
                    if (dialog is DayEventsWindow day)
                    {
                        dayCount++;
                        var list = (ListBox)day.FindName("ScheduleListBox");
                        list.ItemsSource = new[] { new ScheduleItem { EventName = "去上课", Note = "一教201", EventTime = new TimeOnly(9, 29) } };
                        day.UpdateLayout();
                        Require(Controls<Button>((DependencyObject)day.Content).Where(b => b.Style == day.FindResource("DayActionStyle")).All(b => b.ActualWidth == 60), "all day actions have 60 DIP integer width");
                        var texts = Texts((DependencyObject)day.Content).ToArray();
                        Require(texts.All(t => t.FontWeight == FontWeights.Normal && t.FontSize == 14), "day view text uses normal 14-point font");
                        Require(!texts.Any(t => t.Text == "今日日程"), "redundant day heading removed");
                        if (owner.ActualWidth == 400) Render(day);
                        _ = ScheduleEditWindow.ShowEditor(day, new DateTime(2026, 10, 26), null);
                    }
                    else
                    {
                        editorCount++;
                        InteractionChecks.CheckEditorLayout((ScheduleEditWindow)dialog);
                        var combo = (ComboBox)dialog.FindName("DayCombo");
                        var root = (FrameworkElement)dialog.Content;
                        Require(combo.TransformToAncestor(root).Transform(new Point(combo.ActualWidth, combo.ActualHeight)).X <= root.ActualWidth,
                            "editor date controls fit actual width");
                    }
                }
                catch (Exception ex) { failure ??= ex; }
                finally { dialog.Close(); }
            }));
        }));
        try
        {
            foreach (double width in new[] { 400d, 380d, 520d })
            {
                var owner = new Window
                {
                    Width = width, Height = 460, Left = 100, Top = 100,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Opacity = 0, ShowInTaskbar = false, ShowActivated = false
                };
                try
                {
                    owner.Show();
                    _ = DayEventsWindow.ShowForDate(owner, new DateTime(2026, 10, 26), service);
                    if (failure is not null) throw failure;
                }
                finally { owner.Close(); }
            }
            Require(dayCount == 3 && editorCount == 3, "all six native modal positions verified");
            Console.WriteLine("PASS: modal positioning/fonts at owner widths 380, 400 and 520");
        }
        finally { enabled = false; }
    }
    private static void Require(bool valid, string message)
    {
        if (!valid) throw new Exception("FAIL: " + message);
    }
    private static IEnumerable<T> Controls<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i=0; i<VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);
            if (child is T match) yield return match;
            foreach (var nested in Controls<T>(child)) yield return nested;
        }
    }
    private static IEnumerable<TextBlock> Texts(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock text) yield return text;
            foreach (var nested in Texts(child)) yield return nested;
        }
    }
    private static void Render(Window window)
    {
        var root = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
        bitmap.Render(background); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "previews"));
        using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "previews", "day-events.png"));
        encoder.Save(stream);
    }
}
