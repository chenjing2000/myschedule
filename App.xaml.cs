using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace OneSchedule;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;
    private bool _ownsMutex;
    private TrayService? _tray;
    internal SettingsService Settings { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Local namespace separates desktop sessions; SID separates users within a session.
        string identity = @"Local\OneSchedule." + WindowsIdentity.GetCurrent().User!.Value;
        if (e.Args.Contains("--test-host"))
            identity += ".Test." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AppContext.BaseDirectory)));
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
                Dispatcher.BeginInvoke(RestoreMainWindow);
        }, null, Timeout.Infinite, false);
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        bool testHost = e.Args.Contains("--test-host");
        Settings = testHost
            ? new SettingsService(Path.Combine(AppContext.BaseDirectory, "OneSchedule.settings.json"), _ => { })
            : new SettingsService();
        bool settingsLoaded = Settings.Load();
        MainWindow = new MainWindow();
        _tray = new TrayService(MainWindow, ExitFromTray);
        if (e.Args.Contains("--startup"))
        {
            // Show once so Loaded completes normal initialization and starts reminder timers.
            MainWindow.ShowInTaskbar = false;
            MainWindow.Opacity = 0;
            MainWindow.ShowActivated = false;
            RoutedEventHandler hideAfterLoaded = null!;
            hideAfterLoaded = (_, _) =>
            {
                MainWindow.Loaded -= hideAfterLoaded;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    // A manual activation may already have restored taskbar visibility during startup.
                    if (!MainWindow.ShowInTaskbar) _tray?.Hide();
                    MainWindow.Opacity = 1;
                    MainWindow.ShowActivated = true;
                }));
            };
            MainWindow.Loaded += hideAfterLoaded;
        }
        MainWindow.Show();
        if (!settingsLoaded)
            MessageBox.Show(MainWindow, Settings.LastError, "OneSchedule 设置错误", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    internal void RestoreMainWindow() => _tray?.Restore();

    private void ExitFromTray()
    {
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _activationWait?.Unregister(null);
        _activationEvent?.Dispose();
        if (_ownsMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "OneSchedule 运行错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
