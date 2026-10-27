using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OneSchedule;

internal static class TrayMenuChecks
{
    public static void Run()
    {
        var window = new Window { Width=240, Height=140, Opacity=0, ShowInTaskbar=false };
        bool exited=false;
        var tray=new TrayService(window,()=>exited=true);
        try
        {
            window.Show();tray.Hide();
            var value=typeof(TrayService).GetField("_menu",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(tray);
            Require(value is ContextMenu,"tray uses a WPF context menu");
            var menu=(ContextMenu)value!;
            menu.Opacity=0;
            Require(menu.Items.Cast<MenuItem>().Select(i=>i.Header?.ToString()).SequenceEqual(new[] { "打开","退出" }),"menu retains exactly Open and Exit");
            tray.ShowMenu();Pump();
            Require(menu.IsOpen && !window.IsVisible && !window.ShowInTaskbar,"menu opens while main window stays hidden");
            Require(menu.FontSize==14 && menu.FontWeight==FontWeights.Normal,"menu uses normal 14 DIP text");
            Require(Math.Abs(menu.ActualWidth - 64) < .01, "tray menu has fixed integer width of 64 DIP");
            foreach (MenuItem item in menu.Items)
            {
                var text = Children<TextBlock>(item).Single();
                var itemBounds = item.TransformToAncestor(menu).TransformBounds(new Rect(item.RenderSize));
                var textBounds = text.TransformToAncestor(menu).TransformBounds(new Rect(text.RenderSize));
                var formatted = new FormattedText(item.Header.ToString(), System.Globalization.CultureInfo.GetCultureInfo("zh-CN"),
                    FlowDirection.LeftToRight, new Typeface(menu.FontFamily, menu.FontStyle, menu.FontWeight, menu.FontStretch),
                    menu.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(menu).PixelsPerDip);
                Require(formatted.Width <= text.ActualWidth + .1 && textBounds.Left >= itemBounds.Left && textBounds.Right <= itemBounds.Right && itemBounds.Right <= menu.ActualWidth,
                    "complete menu text fits within item and popup bounds");
            }

            var outer=Children<Border>(menu).Single(b=>b.CornerRadius==new CornerRadius(8) && b.Effect is DropShadowEffect);
            var preview=new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth),(int)Math.Ceiling(menu.ActualHeight),96,96,PixelFormats.Pbgra32);
            preview.Render(outer);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(preview));
            string folder=System.IO.Path.Combine(AppContext.BaseDirectory,"previews");System.IO.Directory.CreateDirectory(folder);
            using(var stream=System.IO.File.Create(System.IO.Path.Combine(folder,"tray-menu.png"))) png.Save(stream);
            var first=(MenuItem)menu.Items[0];
            Require(Children<Border>(first).Any(b=>b.CornerRadius==new CornerRadius(4)),"item highlight has rounded corners");
            Press(menu,Key.Escape);Pump();
            Require(!menu.IsOpen,"Escape dismisses menu");
            OutsideClick(tray,menu);
            Require(!menu.IsOpen,"real outside click dismisses menu");
            ScreenEdge(tray,menu);
            tray.ShowMenu();Pump();
            Press(menu,Key.Down);Pump();
            Require(first.IsHighlighted || first.IsKeyboardFocusWithin,"Down selects the first action");
            Press(first,Key.Enter);Pump();
            Require(!menu.IsOpen && window.IsVisible && window.ShowInTaskbar,"Enter on Open restores main window and dismisses menu");
            tray.Hide();tray.ShowMenu();Pump();
            ((MenuItem)menu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump();
            Require(exited && !menu.IsOpen,"Exit invokes shutdown callback and dismisses menu");
            tray.ShowMenu();Pump();tray.Dispose();
            Require(!menu.IsOpen,"disposing tray closes an open menu");
        }
        finally { tray.Dispose();window.Close(); }
        Console.WriteLine("PASS: rounded WPF tray menu, hidden-window open, keyboard actions and disposal");
    }
    private static void ScreenEdge(TrayService tray,ContextMenu menu)
    {
        GetCursorPos(out var previous);
        var monitor=new MonitorInfo { Size=System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
        Require(GetMonitorInfo(MonitorFromPoint(previous,2),ref monitor),"monitor work area is available");
        try
        {
            SetCursorPos(monitor.Work.Right-1,monitor.Work.Bottom-1);tray.ShowMenu();Pump();
            GetWindowRect(((HwndSource)PresentationSource.FromVisual(menu)).Handle,out var bounds);
            Require(bounds.Left>=monitor.Work.Left && bounds.Top>=monitor.Work.Top && bounds.Right<=monitor.Work.Right && bounds.Bottom<=monitor.Work.Bottom,
                "menu stays inside current monitor work area near bottom-right edge");
        }
        finally { menu.IsOpen=false;SetCursorPos(previous.X,previous.Y);Pump(); }
    }
    private static void OutsideClick(TrayService tray,ContextMenu menu)
    {
        var outside=new Window { Left=SystemParameters.WorkArea.Left+30,Top=SystemParameters.WorkArea.Top+30,
            Width=320,Height=200,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,
            Background=Brushes.White,Opacity=.01,Topmost=true,ShowActivated=false,ShowInTaskbar=false };
        GetCursorPos(out var previous);
        try
        {
            outside.Show();tray.ShowMenu();Pump();
            Require(menu.IsOpen,"menu opens before native outside click");
            GetWindowRect(((HwndSource)PresentationSource.FromVisual(menu)).Handle,out var bounds);
            var point=new[] { new Point(20,20),new Point(300,20),new Point(20,180),new Point(300,180) }
                .Select(outside.PointToScreen).First(p=>p.X<bounds.Left || p.X>=bounds.Right || p.Y<bounds.Top || p.Y>=bounds.Bottom);
            // Click only the blank test window, then restore the user's cursor.
            SetCursorPos((int)point.X,(int)point.Y);
            mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);Pump();
        }
        finally { SetCursorPos(previous.X,previous.Y);outside.Close();Pump(); }
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint { public int X,Y; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left,Top,Right,Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size;public NativeRect Monitor,Work;public uint Flags; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point,uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetMonitorInfoW")] private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetCursorPos(int x,int y);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
    private static void Press(UIElement element,Key key) => element.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(element),0,key) { RoutedEvent=Keyboard.KeyDownEvent });
    private static void Pump() { var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame); }
    private static void Require(bool condition,string message) { if(!condition)throw new Exception(message); }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) { var child=VisualTreeHelper.GetChild(root,i);if(child is T match)yield return match;foreach(var nested in Children<T>(child))yield return nested; }
    }
}
