using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OneSchedule;

internal static class ReminderEditorChecks
{
    public static void Run()
    {
        foreach (double width in new[] { 380d,400d,520d })
        {
            var editor=Create(null); editor.Width=width;
            try
            {
                editor.Opacity=0; editor.ShowInTaskbar=false; editor.Show(); editor.UpdateLayout();
                var frame=(Border)editor.FindName("ReminderSettingsBorder");
                Require(frame.CornerRadius.TopLeft==8 && frame.BorderThickness.Left==1,"rounded bordered settings group");
                var bounds=frame.TransformToAncestor(editor).TransformBounds(new Rect(frame.RenderSize));
                Require(bounds.Left>=0 && bounds.Right<=editor.ActualWidth && bounds.Bottom<=editor.ActualHeight,"group fits editor at "+width);
                string[] fields=["RemindMinutesTextBox","ReminderCountTextBox","ReminderIntervalTextBox","ReminderDurationTextBox"];
                string[] defaults=["30","3","3","1"];
                for (int i=0;i<fields.Length;i++)
                {
                    var box=Find(editor,fields[i]);
                    Require(box.Text==defaults[i] && box.TextAlignment==TextAlignment.Center && box.ActualWidth==(i==0 ? 42 : 30),"numeric defaults and integer width: "+fields[i]);
                    Require(!InputMethod.GetIsInputMethodEnabled(box) && !box.AllowDrop,"numeric IME and drop restrictions");
                    var paste=new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText,"1.5"),false,DataFormats.UnicodeText);
                    box.RaiseEvent(paste); Require(paste.CommandCancelled,"noninteger paste rejected");
                }
                Find(editor,"EventNameTextBox").Text="test";
                Require(Build(editor,out var result) && result.RemindingMinutes==30 && result.ReminderCount==3 && result.ReminderIntervalMinutes==3 && result.ReminderDurationMinutes==1,"new editor builds defaults");
                foreach (var field in fields)
                {
                    var box=Find(editor,field); string original=box.Text;
                    foreach (string invalid in new[] { "", "-1", "1.5", " 2", "2 ", "三", "100" })
                    {
                        box.Text=invalid; Require(!Build(editor,out _),"invalid numeric save: "+field+"="+invalid);
                    }
                    box.Text=original;
                }
                Find(editor,"ReminderIntervalTextBox").Text="1"; Find(editor,"ReminderDurationTextBox").Text="2";
                Require(!Build(editor,out _),"overlapping rounds rejected");
                Find(editor,"ReminderCountTextBox").Text="1";
                Require(!Find(editor,"ReminderIntervalTextBox").IsEnabled && Build(editor,out _),"single round ignores interval");
                Find(editor,"ReminderCountTextBox").Text="01";
                Require(!Find(editor,"ReminderIntervalTextBox").IsEnabled,"numeric one disables interval even with a leading zero");
                Find(editor,"ReminderCountTextBox").Text="0"; Require(!Build(editor,out _),"zero count rejected");
            }
            finally { editor.Close(); }
        }
        var existing=new ScheduleItem { Id=Guid.NewGuid().ToString(), EventName="existing", Calendar=CalendarKind.Solar,
            StartDate=new ScheduleDate { Year=2026,Month=11,Day=2 }, EventTime=new TimeOnly(9,0), RemindingMinutes=37,
            ReminderCount=4, ReminderIntervalMinutes=9, ReminderDurationMinutes=2, CompletedReminderDates=[new DateOnly(2026,11,2)] };
        var edit=Create(existing);
        try
        {
            edit.Opacity=0; edit.ShowInTaskbar=false; edit.Show(); edit.UpdateLayout();
            Require(Find(edit,"RemindMinutesTextBox").Text=="37" && Find(edit,"ReminderCountTextBox").Text=="4" && Find(edit,"ReminderIntervalTextBox").Text=="9" && Find(edit,"ReminderDurationTextBox").Text=="2","editing retains settings");
            Find(edit,"NoteTextBox").Text="changed";
            Require(Build(edit,out var metadata) && metadata.CompletedReminderDates.Contains(new DateOnly(2026,11,2)),"metadata edit keeps completion");
            Find(edit,"ReminderIntervalTextBox").Text="8";
            Require(Build(edit,out var changed) && changed.CompletedReminderDates.Count==0,"reminder configuration edit resets completion");
        }
        finally { edit.Close(); }
        SimultaneousBanner();
        Console.WriteLine("PASS: rounded reminder settings, 30/3/3/1 defaults, strict numeric inputs and editor widths 380/400/520");
    }
    private static void SimultaneousBanner()
    {
        string folder=System.IO.Path.Combine(AppContext.BaseDirectory,"banner-fixture",Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(folder);
        var main=new MainWindow { Width=380,Height=440,Opacity=0,ShowInTaskbar=false };
        try
        {
            var service=new ScheduleService(System.IO.Path.Combine(folder,"events.json"));
            Require(service.Reload(),"banner fixture loads");
            for (int i=0;i<3;i++)
                Require(service.Add(new ScheduleItem { Id=Guid.NewGuid().ToString(),EventName="同时提醒项目"+i,Calendar=CalendarKind.Solar,
                    StartDate=new ScheduleDate { Year=2026,Month=11,Day=2 },EventTime=new TimeOnly(9,0) }),"banner event saved");
            main.Show(); main.UpdateLayout();
            var field=typeof(MainWindow).GetField("_reminderService",BindingFlags.Instance|BindingFlags.NonPublic)!;
            (field.GetValue(main) as ReminderService)?.Dispose();
            var now=new DateTime(2026,11,2,8,30,10);
            using var alarm=new ReminderService(service,main,_=>{},item=>typeof(MainWindow).GetMethod("OnReminderDue",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,[item]),null,()=>now,false);
            field.SetValue(main,alarm);
            alarm.ProcessAt(now); main.UpdateLayout();
            var banner=(Border)main.FindName("ReminderBar");
            Require(banner.TransformToAncestor(main).TransformBounds(new Rect(banner.RenderSize)).Bottom<=main.ActualHeight,"three simultaneous reminders fit the supported window");
            var text=(TextBlock)main.FindName("ReminderText");
            Require(text.Text.Contains("同时提醒项目0") && text.Text.Contains("同时提醒项目1") && text.Text.Contains("同时提醒项目2"),"all three active events appear in the simple banner");
            text.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left) { RoutedEvent=UIElement.PreviewMouseDownEvent });
            Require(!alarm.IsAlertActive,"clicking reminder content still acknowledges active events");
        }
        finally { main.Close(); System.IO.Directory.Delete(folder,true); }
    }
    private static ScheduleEditWindow Create(ScheduleItem? item) => (ScheduleEditWindow)Activator.CreateInstance(typeof(ScheduleEditWindow),BindingFlags.Instance|BindingFlags.NonPublic,null,[new DateTime(2026,11,2),item],null)!;
    private static TextBox Find(Window window,string name) => (TextBox)window.FindName(name);
    private static bool Build(ScheduleEditWindow window,out ScheduleItem item)
    {
        object?[] args=[null,null]; bool valid=(bool)typeof(ScheduleEditWindow).GetMethod("TryBuildResult",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,args)!;
        item=(ScheduleItem)args[0]!; return valid;
    }
    private static void Require(bool condition,string message) { if (!condition) throw new Exception(message); }
}
