using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OneSchedule;

internal static class DigitMarkerChecks
{
    public static void Run()
    {
        var editor = (ScheduleEditWindow)Activator.CreateInstance(typeof(ScheduleEditWindow), BindingFlags.Instance | BindingFlags.NonPublic, null, [new DateTime(2026,10,26), null], null)!;
        try
        {
            editor.Opacity=0; editor.ShowInTaskbar=false; editor.Show(); editor.UpdateLayout();
            ((ComboBox)editor.FindName("RecurrenceCombo")).SelectedValue="Yearly";
            var box=(TextBox)editor.FindName("RepeatCountTextBox");
            var space=new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box), 0, Key.Space) { RoutedEvent=Keyboard.PreviewKeyDownEvent };
            box.RaiseEvent(space);
            Require(space.Handled, "space key must be blocked");
            Require(!InputMethod.GetIsInputMethodEnabled(box), "numeric input disables IME");
            foreach(string text in new[] { "三", "a", " ", ".", "-", "+", "１２", "١" })
            {
                var input=new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current,box,text)) { RoutedEvent=TextCompositionManager.PreviewTextInputEvent };
                box.RaiseEvent(input); Require(input.Handled,"invalid typing: "+text);
                box.SelectAll(); Require(Paste(box,text).CommandCancelled,"invalid paste: "+text);
            }
            foreach(string text in new[] { "0", "9", "100", "999" })
            {
                box.SelectAll(); Require(!Paste(box,text).CommandCancelled,"valid paste: "+text);
            }
            box.Text="12"; box.Select(1,1); Require(!Paste(box,"99").CommandCancelled,"replacement fits three digits");
            box.Select(2,0); Require(Paste(box,"99").CommandCancelled,"overflow paste rejected whole");
            ((TextBox)editor.FindName("EventNameTextBox")).Text="valid";
            foreach(string text in new[] { "0", "1000", "+2", "-2", "2.0", " 2", "2 ", "三" })
            {
                box.Text=text; object?[] args=[null,null];
                Require(!(bool)typeof(ScheduleEditWindow).GetMethod("TryBuildResult",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(editor,args)!,"invalid saved count: "+text);
            }
        }
        finally { editor.Close(); }
        var view=new CalendarView();
        var date=new DateTime(2026,10,26);
        string[] colors=["#A477E8","#39A9EB","#41C984","#F5AC3D","#F45D75"];
        var periods=new[] { RepeatPeriod.None, RepeatPeriod.Daily, RepeatPeriod.Weekly, RepeatPeriod.Monthly, RepeatPeriod.Yearly };
        for(int i=0;i<periods.Length;i++)
        {
            view.Rebuild(2026,10,[Item(periods[i],9)]); Layout(view);
            var marker=Children<Polygon>(view).Single(p=>p.DataContext is CalendarDayViewModel day && day.Date==date);
            Require(marker.Visibility==Visibility.Visible && ((SolidColorBrush)marker.Fill).Color==(Color)ColorConverter.ConvertFromString(colors[i]),"marker color: "+periods[i]);
        }
        view.Rebuild(2026,10,[Item(RepeatPeriod.Yearly,16),Item(RepeatPeriod.Weekly,8)]); Layout(view);
        var mixed=Children<Polygon>(view).Single(p=>p.DataContext is CalendarDayViewModel day && day.Date==date);
        Require(((SolidColorBrush)mixed.Fill).Color==(Color)ColorConverter.ConvertFromString(colors[2]),"earliest event controls marker");
        Require(((CalendarDayViewModel)mixed.DataContext).TooltipText!.Contains("Yearly"),"tooltip retains other events");
        Require(Children<Polygon>(view).Any(p=>p.DataContext is CalendarDayViewModel { HasSchedule:false } && p.Visibility==Visibility.Collapsed),"empty dates hide marker");
        Console.WriteLine("PASS: digit filtering and five recurrence marker colors");
    }
    private static ScheduleItem Item(RepeatPeriod period,int hour)=>new() { Id=Guid.NewGuid().ToString(), Calendar=CalendarKind.Solar, StartDate=new ScheduleDate { Year=2026,Month=10,Day=26 }, Recurrence=period, RepeatCount=ScheduleItem.DefaultRepeatCount(period),EventTime=new TimeOnly(hour,0),EventName=period.ToString() };
    private static DataObjectPastingEventArgs Paste(TextBox box,string text) { var e=new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText,text),false,DataFormats.UnicodeText); box.RaiseEvent(e); return e; }
    private static void Layout(FrameworkElement view) { view.Measure(new Size(344,320));view.Arrange(new Rect(0,0,344,320));view.UpdateLayout(); }
    private static void Require(bool ok,string message) { if(!ok) throw new Exception(message); }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject { for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) { var child=VisualTreeHelper.GetChild(root,i);if(child is T match) yield return match; foreach(var nested in Children<T>(child)) yield return nested; } }
}
