using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OneSchedule;

internal static class NumberInputChecks
{
    public static void Run()
    {
        var editor = (ScheduleEditWindow)Activator.CreateInstance(typeof(ScheduleEditWindow), BindingFlags.Instance | BindingFlags.NonPublic, null,
            [new DateTime(2026,10,26), null], null)!;
        try
        {
            editor.Opacity = 0; editor.ShowInTaskbar = false; editor.Width = 380; editor.Show(); Pump();
            ((ComboBox)editor.FindName("RecurrenceCombo")).SelectedValue = "Yearly";
            foreach (var setup in new[] { ("RepeatCountTextBox", "100"), ("RepeatCountTextBox", "999"), ("RemindMinutesTextBox", "99") })
            {
                var box = (TextBox)editor.FindName(setup.Item1);
                box.Text = setup.Item2;
                foreach (bool focused in new[] { false, true })
                foreach (int position in new[] { 0, box.Text.Length })
                {
                    if (focused) box.Focus(); else ((Button)editor.FindName("CalendarTypeButton")).Focus();
                    box.Select(position, 0); Pump();
                    Require(box.IsKeyboardFocused == focused, "numeric focus state must match the scenario");
                    var viewport = Children<ScrollContentPresenter>(box).Single();
                    var bounds = viewport.TransformToAncestor(box).TransformBounds(new Rect(viewport.RenderSize));
                    var first = box.GetRectFromCharacterIndex(0, false);
                    var last = box.GetRectFromCharacterIndex(box.Text.Length - 1, true);
                    Console.WriteLine($"NUMERIC: {setup.Item1}={box.Text}, focused={focused}, caret={position}, offset={box.HorizontalOffset:F2}, viewport={bounds.Width:F2}, first={first.Left:F2}, last={last.Left:F2}");
                    Require(!first.IsEmpty && !last.IsEmpty && first.Left >= bounds.Left - .5 && last.Left <= bounds.Right + .5 && box.HorizontalOffset <= .5,
                        "all numeric digits and trailing caret fit without horizontal scrolling");
                }
            }
        }
        finally { editor.Close(); }
        Console.WriteLine("PASS: numeric text visibility and caret checks");
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue=false));
        Dispatcher.PushFrame(frame);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
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
