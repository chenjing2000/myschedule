using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OneSchedule;

internal static class WeekdayChecks
{
    public static void Run()
    {
        var service=new ScheduleService(System.IO.Path.Combine(AppContext.BaseDirectory,"unused-weekday-fixture.json"));
        var day=(DayEventsWindow)Activator.CreateInstance(typeof(DayEventsWindow),BindingFlags.Instance|BindingFlags.NonPublic,null,[new DateTime(2026,10,26),service],null)!;
        try
        {
            Show(day,380);
            Require(((TextBlock)day.FindName("DateText")).Text.EndsWith(" 星期一"),"day header includes viewed date weekday");
            var buttons=Children<Button>((DependencyObject)day.Content).Where(b=>b.Style==day.FindResource("DayActionStyle")).ToArray();
            Require(buttons.Select(b=>b.Content?.ToString()).SequenceEqual(new[] { "新增","删除" }),"day actions contain only Add and Delete");
            Require(Grid.GetColumn(buttons[0])==1 && Grid.GetColumn(buttons[1])==2,"day buttons move into the two right slots");
        }
        finally { day.Close(); }
        foreach(double width in new[] { 380d,400d,520d })
        {
            var editor=Editor(new DateTime(2026,1,31),null);
            try
            {
                Show(editor,width); Check(editor);
                ((ComboBox)editor.FindName("MonthCombo")).SelectedIndex=1;
                Require((int)((ComboBox)editor.FindName("DayCombo")).SelectedValue==28,"month change clamps January31 to February28");
                Check(editor);
                ((ComboBox)editor.FindName("YearCombo")).SelectedValue=2028; Check(editor);
                foreach(int value in Enumerable.Range(1,7)) { ((ComboBox)editor.FindName("DayCombo")).SelectedValue=value; Check(editor); }
                string previous=((TextBlock)editor.FindName("WeekdayText")).Text;
                ((Button)editor.FindName("CalendarTypeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(editor);
                Require(((TextBlock)editor.FindName("WeekdayText")).Text==previous,"calendar switch preserves actual date weekday");
                ((ComboBox)editor.FindName("DayCombo")).SelectedValue=8; Check(editor);
            }
            finally { editor.Close(); }
        }
        var lunar=Editor(new DateTime(2023,3,22),new ScheduleItem { Calendar=CalendarKind.Lunar, StartDate=new ScheduleDate { Year=2023,Month=2,Day=1,IsLeapMonth=true }, EventName="leap" });
        try { Show(lunar,380); Check(lunar); Require(((TextBlock)lunar.FindName("WeekdayText")).Text=="星期三","editing leap lunar month uses actual solar weekday"); }
        finally { lunar.Close(); }
        Console.WriteLine("PASS: day actions and dynamic solar/lunar weekdays at 380,400,520 DIP");
    }
    private static ScheduleEditWindow Editor(DateTime date,ScheduleItem? item)=>(ScheduleEditWindow)Activator.CreateInstance(typeof(ScheduleEditWindow),BindingFlags.Instance|BindingFlags.NonPublic,null,[date,item],null)!;
    private static void Check(ScheduleEditWindow editor)
    {
        Pump();
        var date=(ScheduleDate)typeof(ScheduleEditWindow).GetField("_date",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(editor)!;
        var calendar=(CalendarKind)typeof(ScheduleEditWindow).GetField("_calendar",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(editor)!;
        var text=(TextBlock?)editor.FindName("WeekdayText");
        Require(text is not null,"editor weekday label exists");
        Require(text!.Text==date.ToSolar(calendar).ToString("dddd",CultureInfo.GetCultureInfo("zh-CN")),"weekday matches final selected actual date");
        var root=(FrameworkElement)editor.Content;
        var bounds=text.TransformToAncestor(root).TransformBounds(new Rect(text.RenderSize));
        Require(text.Margin.Left==6 && bounds.Right<=root.ActualWidth+.1,"weekday fits minimum editor width with 6 DIP gap");
    }
    private static void Show(Window window,double width) { window.Width=width;window.Opacity=0;window.ShowInTaskbar=false;window.Show();Pump(); }
    private static void Pump() { var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame); }
    private static void Require(bool ok,string message) { if(!ok) throw new Exception(message); }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) { var child=VisualTreeHelper.GetChild(root,i);if(child is T match) yield return match;foreach(var nested in Children<T>(child)) yield return nested; }
    }
}
