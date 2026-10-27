using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OneSchedule;

internal static class ScrollBarChecks
{
    public static void Run()
    {
        var failures = new List<Exception>();
        foreach (var check in new Action[] { MainWindowScroll, DayList, BothDirections, TextInput })
        {
            try { check(); Console.WriteLine("PASS: shared scrollbar " + check.Method.Name); }
            catch (Exception ex) { failures.Add(ex); Console.WriteLine("FAIL: shared scrollbar " + check.Method.Name + ": " + ex.Message); }
        }
        if (failures.Count > 0) throw new AggregateException(failures);
    }

    private static void MainWindowScroll()
    {
        var window = new MainWindow();
        try
        {
            Show(window);
            var scroll = Children<ScrollViewer>(window).Single();
            ((FrameworkElement)scroll.Content).MinHeight = 1000;
            Pump();
            Check(scroll, Orientation.Vertical);
        }
        finally { window.Close(); }
    }

    private static void DayList()
    {
        var service = new ScheduleService(System.IO.Path.Combine(AppContext.BaseDirectory, "unused-scroll-fixture.json"));
        var window = (DayEventsWindow)Activator.CreateInstance(typeof(DayEventsWindow), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [new DateTime(2026,10,26), service], null)!;
        try
        {
            var list = (ListBox)window.FindName("ScheduleListBox");
            list.ItemsSource = Enumerable.Range(0,40).Select(i => new ScheduleItem { EventName = "Event " + i }).ToList();
            Show(window);
            var scroll = Children<ScrollViewer>(list).Single();
            Check(scroll, Orientation.Vertical);
            scroll.ScrollToEnd(); Pump();
            Require(scroll.VerticalOffset > 0, "last list item remains reachable");
        }
        finally { window.Close(); }
    }

    private static void BothDirections()
    {
        var scroll = new ScrollViewer { Width=200, Height=140, HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto, Content=new Border { Width=900, Height=900 } };
        var window = new Window { Content=scroll, Width=260, Height=210 };
        try
        {
            Show(window);
            Check(scroll, Orientation.Vertical);
            Check(scroll, Orientation.Horizontal);
            scroll.Content = new Border { Width=20, Height=20 }; Pump();
            Require(Children<ScrollBar>(scroll).All(b => b.Visibility != Visibility.Visible), "short content hides both scrollbars");
        }
        finally { window.Close(); }
    }

    private static void TextInput()
    {
        var box = new TextBox { Width=220, Height=100, AcceptsReturn=true, TextWrapping=TextWrapping.NoWrap,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Auto, VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            Style=(Style)Application.Current.FindResource("RoundedTextBoxStyle"),
            Text=string.Join(Environment.NewLine, Enumerable.Repeat(new string('A',100),30)) };
        var window = new Window { Content=box, Width=280, Height=160 };
        try
        {
            Show(window);
            var scroll=Children<ScrollViewer>(box).Single();
            Check(scroll, Orientation.Vertical);
            Check(scroll, Orientation.Horizontal);
        }
        finally { window.Close(); }
    }

    private static void Check(ScrollViewer scroll, Orientation direction)
    {
        scroll.ScrollToHome(); Pump();
        var bar = Children<ScrollBar>(scroll).Single(b => b.Orientation==direction);
        Require(bar.Visibility==Visibility.Visible, direction + " scrollbar appears for overflow");
        double thickness=direction==Orientation.Vertical ? bar.ActualWidth : bar.ActualHeight;
        Require(Math.Abs(thickness-6)<.1, direction + " scrollbar thickness must be 6 DIP, actual=" + thickness);
        Require(Children<RepeatButton>(bar).Count()==2, "only two invisible page regions, no arrow buttons");
        var command=direction==Orientation.Vertical ? ScrollBar.PageDownCommand : ScrollBar.PageRightCommand;
        var page=Children<RepeatButton>(bar).Single(b => b.Command==command);
        ((RoutedCommand)page.Command).Execute(null,page); Pump();
        Require(Offset(scroll,direction)>0, direction + " track pages forward");
        scroll.ScrollToHome(); Pump();
        var thumb=Children<Thumb>(bar).Single();
        thumb.RaiseEvent(new DragStartedEventArgs(0,0) { RoutedEvent=Thumb.DragStartedEvent });
        double x=direction==Orientation.Horizontal ? 12 : 0, y=direction==Orientation.Vertical ? 12 : 0;
        thumb.RaiseEvent(new DragDeltaEventArgs(x,y) { RoutedEvent=Thumb.DragDeltaEvent });
        thumb.RaiseEvent(new DragCompletedEventArgs(x,y,false) { RoutedEvent=Thumb.DragCompletedEvent }); Pump();
        Require(Offset(scroll,direction)>0, direction + " thumb drag scrolls forward");
        var border=Children<Border>(thumb).Single();
        Require(border.CornerRadius.TopLeft==3, "shared rounded thumb");
        var hoverKey=(DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        thumb.SetValue(hoverKey,true); Pump();
        Require(((SolidColorBrush)border.Background).Color==(Color)ColorConverter.ConvertFromString("#8B9CB4"),"shared hover color");
        thumb.SetValue(hoverKey,false);
    }

    private static double Offset(ScrollViewer scroll,Orientation direction) => direction==Orientation.Vertical ? scroll.VerticalOffset : scroll.HorizontalOffset;
    private static void Show(Window window) { window.Opacity=0; window.ShowInTaskbar=false; window.Show(); Pump(); }
    private static void Pump() { var frame=new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false)); Dispatcher.PushFrame(frame); }
    private static void Require(bool ok,string message) { if(!ok) throw new Exception(message); }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);
            if(child is T match) yield return match;
            foreach(var nested in Children<T>(child)) yield return nested;
        }
    }
}
