using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace OneSchedule;

public sealed class ReminderService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _flashTimer;
    private readonly ScheduleService _scheduleService;
    private readonly Window _window;
    private readonly Action<bool> _setCalendarFlash;
    private readonly Action<ScheduleItem> _onFire;
    private readonly AlarmService _alarm = new();
    private bool _flashDark;
    private readonly Action<string>? _onError;

    public bool IsAlertActive { get; private set; }

    public ReminderService(
        ScheduleService scheduleService,
        Window window,
        Action<bool> setCalendarFlash,
        Action<ScheduleItem> onFire,
        Action<string>? onError = null)
    {
        _scheduleService = scheduleService;
        _window = window;
        _setCalendarFlash = setCalendarFlash;
        _onFire = onFire;
        _onError = onError;

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _timer.Start();

        _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _flashTimer.Tick += OnFlashTick;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_scheduleService.CanSave || _scheduleService.Items.Count == 0) return;
        var now = DateTime.Now;
        var fire = FindDue(_scheduleService.Items.Where(_scheduleService.CanSaveItem).ToList(), now, out DateOnly occurrenceDate);
        if (fire is null) return;

        var previous = fire.LastRemindedOccurrenceDate;
        fire.LastRemindedOccurrenceDate = occurrenceDate;
        if (!_scheduleService.Save(fire))
        {
            fire.LastRemindedOccurrenceDate = previous;
            _onError?.Invoke("保存提醒记录失败：" + _scheduleService.LastError);
            return;
        }
        _onFire(fire);
        StartAlert();
    }

    private void StartAlert()
    {
        IsAlertActive = true;
        _flashDark = false;
        _setCalendarFlash(false);
        _alarm.Start();
        if (Application.Current is App app && app.MainWindow == _window) app.RestoreMainWindow();
        else
        {
            _window.ShowInTaskbar = true;
            _window.Show();
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        }
        _window.Topmost = true;
        _window.Activate();
        _flashTimer.Start();
        WindowTaskbarFlasher.Set(_window, true);
    }

    public bool DismissAlert()
    {
        if (!IsAlertActive) return false;
        IsAlertActive = false;
        _flashTimer.Stop();
        _alarm.Stop();
        _flashDark = false;
        _setCalendarFlash(false);
        _window.Topmost = false;
        WindowTaskbarFlasher.Set(_window, false);
        return true;
    }

    private void OnFlashTick(object? sender, EventArgs e)
    {
        _flashDark = !_flashDark;
        _setCalendarFlash(_flashDark);
    }

    internal static ScheduleItem? FindDue(IReadOnlyList<ScheduleItem> items, DateTime now, out DateOnly occurrenceDate)
    {
        occurrenceDate = default;
        foreach (var item in items)
        {
            if (item.EventTime is not TimeOnly time) continue;
            // The existing three-minute grace window may span midnight; use target dates.
            var first = now.AddMinutes(item.RemindingMinutes - 3).Date;
            var last = now.AddMinutes(item.RemindingMinutes).Date;
            for (var date = first; date <= last; date = date.AddDays(1))
            {
                if (item.LastRemindedOccurrenceDate == DateOnly.FromDateTime(date) ||
                    !ScheduleOccurrence.IsOnDate(item, date)) continue;
                var reminderTime = date.Add(time.ToTimeSpan()).AddMinutes(-item.RemindingMinutes);
                if (now >= reminderTime && now <= reminderTime.AddMinutes(3))
                {
                    occurrenceDate = DateOnly.FromDateTime(date);
                    return item;
                }
            }
        }
        return null;
    }

    public void Dispose()
    {
        DismissAlert();
        _timer.Stop();
        _timer.Tick -= OnTick;
        _flashTimer.Tick -= OnFlashTick;
    }
}

internal sealed class AlarmService
{
    private const uint SndAsync = 0x0001;
    private const uint SndLoop = 0x0008;
    private const uint SndFilename = 0x00020000;
    private const uint SndPurge = 0x0040;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PlaySound(string? pszSound, IntPtr hmod, uint fdwSound);

    public void Start()
    {
        try
        {
            Stop();
            string path = Path.Combine(Path.GetTempPath(), "oneschedule_remind.wav");
            if (!File.Exists(path)) GenerateWav(path);
            PlaySound(path, IntPtr.Zero, SndAsync | SndLoop | SndFilename);
        }
        catch { }
    }

    public void Stop()
    {
        try { PlaySound(null, IntPtr.Zero, SndPurge); } catch { }
    }

    private static void GenerateWav(string path)
    {
        const int sampleRate = 22050;
        const double total = 2.4;
        int count = (int)(sampleRate * total);
        short[] data = new short[count];

        for (int i = 0; i < count; i++)
        {
            double t = i / (double)sampleRate;
            double m = t % 1.2;
            double freq = 0;
            double amp = 0;
            if (m < 0.3) { freq = 880.0; amp = 0.45; }
            else if (m >= 0.4 && m < 0.7) { freq = 1174.7; amp = 0.45; }

            double fade = 1.0;
            if (m < 0.05) fade = m / 0.05;
            else if (m > 0.25 && m < 0.3) fade = (0.3 - m) / 0.05;
            else if (m > 0.4 && m < 0.45) fade = (m - 0.4) / 0.05;
            else if (m > 0.65 && m < 0.7) fade = (0.7 - m) / 0.05;

            if (freq > 0)
            {
                double value = Math.Sin(2 * Math.PI * freq * t) * amp * Math.Max(0, fade);
                data[i] = (short)(value * short.MaxValue);
            }
        }
        WriteWav(path, sampleRate, data);
    }

    private static void WriteWav(string path, int rate, short[] data)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var bw = new BinaryWriter(fs);
        int dataLength = data.Length * 2;
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataLength);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)1);
        bw.Write(rate);
        bw.Write(rate * 2);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataLength);
        foreach (short sample in data) bw.Write(sample);
    }
}

internal static class WindowTaskbarFlasher
{
    private const uint FlashAll = 3;
    private const uint FlashTimerNoForeground = 12;
    private const uint FlashStop = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    public static void Set(Window window, bool enabled)
    {
        try
        {
            var info = new FlashInfo
            {
                cbSize = (uint)Marshal.SizeOf<FlashInfo>(),
                hwnd = new WindowInteropHelper(window).Handle,
                dwFlags = enabled ? FlashAll | FlashTimerNoForeground : FlashStop,
                uCount = enabled ? uint.MaxValue : 0,
                dwTimeout = 0
            };
            FlashWindowEx(ref info);
        }
        catch { }
    }
}
