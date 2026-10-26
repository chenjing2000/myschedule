using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace OneSchedule;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Local namespace separates desktop sessions; SID separates users within a session.
        string identity = @"Local\OneSchedule." + WindowsIdentity.GetCurrent().User!.Value;
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, identity + ".Activate");
        _instanceMutex = new Mutex(false, identity + ".Instance");
        try { _ownsMutex = _instanceMutex.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsMutex = true; }
        if (!_ownsMutex)
        {
            // Pass the launcher's foreground permission to the already-running application.
            AllowSetForegroundWindow(-1);
            _activationEvent.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        _activationWait = ThreadPool.RegisterWaitForSingleObject(_activationEvent, (_, _) =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(ActivateExistingWindow);
        }, null, Timeout.Infinite, false);
        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private void ActivateExistingWindow()
    {
        if (MainWindow is not { } window) return;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        // An owned modal disables its owner; bring the deepest visible dialog forward instead.
        while (window.OwnedWindows.Cast<Window>().LastOrDefault(owned => owned.IsVisible) is { } owned)
            window = owned;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        SetForegroundWindow(new WindowInteropHelper(window).Handle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationWait?.Unregister(null);
        _activationEvent?.Dispose();
        if (_ownsMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "OneSchedule 运行错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
