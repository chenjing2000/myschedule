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
    private readonly Action<string>? _onError;
    private readonly Action? _onStateChanged;
    private readonly Func<DateTime> _clock;
    private readonly bool _effectsEnabled;
    private readonly AlarmService _alarm = new();
    private readonly HashSet<(ScheduleItem Item, DateOnly Date)> _observed = [];
    private readonly HashSet<(ScheduleItem Item, DateOnly Date)> _pendingCompletion = [];
    private List<ReminderOccurrence> _active = [];
    private bool _flashDark;
    private bool _disposed;

    public bool IsAlertActive { get; private set; }
    internal IReadOnlyList<ReminderOccurrence> ActiveReminders => _active;

    public ReminderService(ScheduleService scheduleService, Window window, Action<bool> setCalendarFlash,
        Action<ScheduleItem> onFire, Action<string>? onError = null, Action? onStateChanged = null)
        : this(scheduleService,window,setCalendarFlash,onFire,onError,() => DateTime.Now,true,onStateChanged) { }

    internal ReminderService(ScheduleService scheduleService, Window window, Action<bool> setCalendarFlash,
        Action<ScheduleItem> onFire, Action<string>? onError, Func<DateTime> clock, bool effectsEnabled,
        Action? onStateChanged = null)
    {
        _scheduleService=scheduleService; _window=window; _setCalendarFlash=setCalendarFlash;
        _onFire=onFire; _onError=onError; _onStateChanged=onStateChanged;
        _clock=clock; _effectsEnabled=effectsEnabled;
        _timer=new DispatcherTimer(DispatcherPriority.Background) { Interval=TimeSpan.FromSeconds(1) };
        _timer.Tick+=OnTick;
        if (effectsEnabled) _timer.Start();
        _flashTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(500) };
        _flashTimer.Tick+=OnFlashTick;
    }

    private void OnTick(object? sender, EventArgs e) => ProcessAt(_clock());

    internal void ProcessAt(DateTime now)
    {
        if (_disposed) return;
        // Refresh may replace instances; carry tracking only to the same ID and reminder rule.
        foreach (var pending in _pendingCompletion.ToList())
        {
            var current=CurrentItem(pending.Item,pending.Date);
            if (ReferenceEquals(current,pending.Item)) continue;
            _pendingCompletion.Remove(pending);
            if (current is not null) _pendingCompletion.Add((current,pending.Date));
        }
        foreach (var observed in _observed.ToList())
        {
            var current=CurrentItem(observed.Item,observed.Date);
            _observed.Remove(observed);
            if (current is null) continue;
            if (now >= ReminderPlan.End(current,observed.Date)) _pendingCompletion.Add((current,observed.Date));
            else _observed.Add((current,observed.Date));
        }
        bool saved=PersistCompletions();
        var active=ReminderPlan.ActiveAt(_scheduleService.Items.Where(_scheduleService.CanSaveItem),now)
            .Where(occurrence => !_pendingCompletion.Contains((occurrence.Item,occurrence.Date))).ToList();
        foreach (var occurrence in active) _observed.Add((occurrence.Item,occurrence.Date));
        bool changed=!_active.SequenceEqual(active);
        _active=active;
        if (_active.Count==0) StopAlert();
        else
        {
            if (!IsAlertActive) StartAlert();
            if (changed) _onFire(_active[^1].Item);
        }
        if (changed || saved) _onStateChanged?.Invoke();
    }

    private ScheduleItem? CurrentItem(ScheduleItem previous, DateOnly date) =>
        _scheduleService.Items.Contains(previous) ? previous : _scheduleService.Items.FirstOrDefault(item =>
            Guid.Parse(item.Id)==Guid.Parse(previous.Id) && ScheduleOccurrence.SameReminderIdentity(previous,item) &&
            ScheduleOccurrence.IsOnDate(item,date.ToDateTime(TimeOnly.MinValue)));

    private bool PersistCompletions()
    {
        bool changed=false;
        foreach (var group in _pendingCompletion.GroupBy(record => record.Item).ToList())
        {
            var item=group.Key;
            if (!_scheduleService.Items.Contains(item))
            {
                foreach (var record in group) _pendingCompletion.Remove(record);
                continue;
            }
            var previous=item.CompletedReminderDates;
            item.CompletedReminderDates=previous.Concat(group.Select(record => record.Date)).Distinct().Order().ToList();
            if (_scheduleService.Save(item))
            {
                foreach (var record in group) _pendingCompletion.Remove(record);
                changed=true;
            }
            else
            {
                item.CompletedReminderDates=previous;
                _onError?.Invoke("保存提醒完成记录失败："+_scheduleService.LastError);
            }
        }
        return changed;
    }

    private void StartAlert()
    {
        IsAlertActive=true;
        _flashDark=false;
        _setCalendarFlash(false);
        if (!_effectsEnabled) return;
        _alarm.Start();
        if (Application.Current is App app && app.MainWindow==_window) app.RestoreMainWindow();
        else
        {
            _window.ShowInTaskbar=true;
            _window.Show();
            if (_window.WindowState==WindowState.Minimized) _window.WindowState=WindowState.Normal;
        }
        _window.Topmost=true;
        _window.Activate();
        _flashTimer.Start();
        WindowTaskbarFlasher.Set(_window,true);
    }

    private void StopAlert()
    {
        if (!IsAlertActive) return;
        IsAlertActive=false;
        _flashTimer.Stop();
        if (_effectsEnabled) _alarm.Stop();
        _flashDark=false;
        _setCalendarFlash(false);
        _window.Topmost=false;
        if (_effectsEnabled) WindowTaskbarFlasher.Set(_window,false);
    }

    public bool DismissAlert()
    {
        if (_disposed || _active.Count==0) return false;
        // Cancel only occurrences ringing now; waiting occurrences retain their later rounds.
        foreach (var occurrence in _active) _pendingCompletion.Add((occurrence.Item,occurrence.Date));
        StopAlert();
        ProcessAt(_clock());
        return true;
    }

    private void OnFlashTick(object? sender, EventArgs e)
    {
        _flashDark=!_flashDark;
        _setCalendarFlash(_flashDark);
    }

    internal static ScheduleItem? FindDue(IReadOnlyList<ScheduleItem> items, DateTime now, out DateOnly occurrenceDate)
    {
        var occurrence=ReminderPlan.ActiveAt(items,now).FirstOrDefault();
        occurrenceDate=occurrence?.Date ?? default;
        return occurrence?.Item;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed=true;
        // Exit is not acknowledgement: future rounds are reconstructed on the next launch.
        StopAlert();
        _timer.Stop(); _timer.Tick-=OnTick;
        _flashTimer.Stop(); _flashTimer.Tick-=OnFlashTick;
        _active.Clear(); _observed.Clear(); _pendingCompletion.Clear();
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
