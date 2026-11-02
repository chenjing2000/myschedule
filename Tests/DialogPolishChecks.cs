using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using OneSchedule;

internal static class DialogPolishChecks
{
    public static void Run()
    {
        EditorLayouts();
        DaySelection();
        Console.WriteLine("PASS: compact reminder row, label colors, six-option dropdowns and transient feathered selection");
    }
    internal static void EditorLayouts()
    {
        foreach (double width in new[] { 380d,400d,520d }) Editor(width);
        Console.WriteLine("PASS: reminder layout at 380/400/520 DIP");
    }
    private static void Editor(double width)
    {
        var editor=(ScheduleEditWindow)Activator.CreateInstance(typeof(ScheduleEditWindow),BindingFlags.Instance|BindingFlags.NonPublic,null,[new DateTime(2026,11,2),null],null)!;
        try
        {
            editor.Width=width; editor.Opacity=0; editor.ShowInTaskbar=false; editor.Show(); editor.UpdateLayout();
            foreach (var name in new[] { "RecurrenceCombo","YearCombo","MonthCombo","DayCombo","HourCombo","MinuteCombo" })
                Require(((ComboBox)editor.FindName(name)).MaxDropDownHeight==180,"editor dropdown viewport must show at most six options");
            var frame=(Border)editor.FindName("ReminderSettingsBorder");
            var names=new[] { "ReminderCountTextBox","ReminderIntervalTextBox","ReminderDurationTextBox" };
            var boxes=names.Select(name=>(TextBox)editor.FindName(name)).ToArray();
            var bounds=boxes.Select(box=>box.TransformToAncestor(frame).TransformBounds(new Rect(box.RenderSize))).ToArray();
            Require(boxes.All(box=>box.ActualWidth==42) && bounds.All(rect=>Math.Abs(rect.Top-bounds[0].Top)<.5),"three reminder fields share one row and 42 DIP widths");
            foreach (var box in boxes) { box.Text="99"; box.CaretIndex=2; box.UpdateLayout(); Require(box.GetRectFromCharacterIndex(1,true).Right<=box.ActualWidth-1,"two digits and caret fit compact field"); }
            foreach (var element in Children<FrameworkElement>(frame).Where(e=>e is TextBox or TextBlock))
            {
                var rect=element.TransformToAncestor(frame).TransformBounds(new Rect(element.RenderSize));
                Require(rect.Right<=frame.ActualWidth-frame.Padding.Right+.5,"all reminder text fits minimum editor width");
            }
            var primary=((Brush)editor.FindResource("TextPrimaryBrush")).ToString();
            var secondary=((Brush)editor.FindResource("TextSecondaryBrush")).ToString();
            foreach (var text in Children<TextBlock>(frame).Where(t=>t.Text is "次，间隔" or "分，时长" or "分"))
                Require(text.Foreground.ToString()==primary,"later labels match first-label color");
            Require(boxes.All(box=>box.Margin.Left==6 && box.Margin.Right==6),"compact inputs have six DIP spacing on both sides");
            Require(Children<TextBlock>(frame).Any(text=>text.Text=="提前") && Children<TextBlock>(frame).Any(text=>text.Text=="提醒"),"new shortened reminder labels");
            Require(Children<Run>(frame).Any(run=>run.Text=="分钟提醒"),"advance unit wording");
            var hint=Children<Run>(frame).Single(run=>run.Text=="（0=准点提醒）");
            Require(hint.Foreground.ToString()==secondary,"zero-minute explanatory text remains secondary");
            if (width==400) Capture((FrameworkElement)editor.Content,"compact-reminder-settings");
        }
        finally { editor.Close(); }
    }
    private static void DaySelection()
    {
        string folder=Path.Combine(AppContext.BaseDirectory,"selection-fixture",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var service=new ScheduleService(Path.Combine(folder,"events.json"));
        Require(service.Reload(),"selection fixture loads");
        foreach (var name in new[] { "first","second" }) Require(service.Add(new ScheduleItem { EventName=name,StartDate=new ScheduleDate { Year=2026,Month=11,Day=2 },EventTime=new TimeOnly(9,0) }),"selection fixture saves");
        var day=(DayEventsWindow)Activator.CreateInstance(typeof(DayEventsWindow),BindingFlags.Instance|BindingFlags.NonPublic,null,[new DateTime(2026,11,2),service],null)!;
        try
        {
            day.Opacity=0; day.ShowInTaskbar=false; day.Show(); day.UpdateLayout();
            var list=(ListBox)day.FindName("ScheduleListBox"); var delete=(Button)day.FindName("DeleteButton");
            Require(list.SelectedItem is null && !delete.IsEnabled,"day list starts without an implicit selection");
            list.SelectedIndex=0; day.UpdateLayout();
            var row=(ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            var layer=row.Template.FindName("SelectionLayer",row) as Border;
            Require(layer is not null && layer.BorderThickness==new Thickness(0) && layer.Effect is BlurEffect && layer.Opacity==1 && row.FocusVisualStyle is null,"selected overlay has soft edges and no border or focus rectangle");
            var hit=row.InputHitTest(new Point(1,row.ActualHeight/2)) as DependencyObject;
            Require(hit is not null && ItemsControl.ContainerFromElement(list,hit)==row,"row-edge whitespace remains part of the event hit area");
            row.RaiseEvent(MouseEvent()); Require(list.SelectedItem is not null,"clicking inside current row preserves selection");
            ((TextBlock)day.FindName("DateText")).RaiseEvent(MouseEvent());
            Require(list.SelectedItem is null && !delete.IsEnabled,"clicking header clears selection and delete state");
            list.SelectedIndex=0; list.RaiseEvent(MouseEvent()); Require(list.SelectedItem is null,"clicking list blank clears selection");
            list.SelectedIndex=0;
            typeof(Window).GetMethod("OnDeactivated",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(day,[EventArgs.Empty]);
            Require(list.SelectedItem is null,"leaving day window clears selection");
            list.SelectedIndex=0; day.UpdateLayout(); Capture((FrameworkElement)day.Content,"feathered-day-selection");
            delete.RaiseEvent(MouseEvent()); Require(list.SelectedItem is not null,"delete command retains target until execution");
            delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(service.Items.Count==1 && service.Items[0].EventName=="second" && list.SelectedItem is null && !delete.IsEnabled,"delete works then clears selection");
        }
        finally { day.Close(); Directory.Delete(folder,true); }
    }
    private static MouseButtonEventArgs MouseEvent() => new(Mouse.PrimaryDevice,0,MouseButton.Left) { RoutedEvent=UIElement.PreviewMouseDownEvent };
    private static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject
    {
        for (int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i); if(child is T value) yield return value;
            foreach(var nested in Children<T>(child)) yield return nested;
        }
        if (root is TextBlock text) foreach(var inline in text.Inlines) if(inline is T value) yield return value;
    }
    private static void Capture(FrameworkElement root,string name)
    {
        string dir=Path.Combine(AppContext.BaseDirectory,"previews"); Directory.CreateDirectory(dir);
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth+root.Margin.Left+root.Margin.Right),(int)Math.Ceiling(root.ActualHeight+root.Margin.Top+root.Margin.Bottom),96,96,PixelFormats.Pbgra32);
        bitmap.Render(root); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(Path.Combine(dir,name+".png")); encoder.Save(stream);
    }
    private static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
}
