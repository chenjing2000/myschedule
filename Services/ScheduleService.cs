using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OneSchedule;

public sealed class ScheduleService
{
    private sealed class Source(string path)
    {
        public string Path { get; } = path;
        public List<ScheduleItem> Items { get; set; } = [];
        public string? LoadedText { get; set; }
        public bool CanSave { get; set; }
    }

    private readonly string _path;
    private readonly bool _directoryMode;
    private List<Source> _sources = [];
    private List<ScheduleItem> _items = [];
    private readonly Dictionary<ScheduleItem, Source> _owners = [];
    private HashSet<Guid> _conflicts = [];

    public ScheduleService(string? filePath = null)
    {
        _directoryMode = filePath is null;
        _path = filePath ?? Path.Combine(AppContext.BaseDirectory, "data");
    }

    private ScheduleService(string directory, bool directoryMode)
    {
        _path = directory;
        _directoryMode = directoryMode;
    }

    public static ScheduleService ForDirectory(string directory) => new(directory, true);
    public IReadOnlyList<ScheduleItem> Items => _items;
    public string LastError { get; private set; } = "";
    public bool CanSave => _sources.Any(source => source.CanSave);
    public bool CanSaveItem(ScheduleItem item) => _owners.TryGetValue(item, out var source) && source.CanSave;

    public bool Reload()
    {
        foreach (var source in _sources) source.CanSave = false;
        try
        {
            if (!_directoryMode)
            {
                var source = ReadSource(_path, allowMissing: true);
                _sources = [source];
                _conflicts.Clear();
                RebuildItems();
                LastError = "";
                return true;
            }

            Directory.CreateDirectory(_path);
            var paths = JsonPaths();
            if (paths.Count == 0)
            {
                string file = Path.Combine(_path, "MySchedule.json");
                try
                {
                    using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write);
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                    writer.Write(ScheduleJson.Serialize([]));
                }
                catch (IOException) when (File.Exists(file)) { }
                paths = JsonPaths();
            }

            var errors = new List<string>();
            var sources = new List<Source>();
            foreach (string path in paths)
            {
                try { sources.Add(ReadSource(path)); }
                catch (Exception ex)
                {
                    sources.Add(new Source(path));
                    errors.Add(Path.GetFileName(path) + "：" + ex.Message);
                }
            }
            _sources = sources;
            _conflicts = sources.SelectMany(source => source.Items).GroupBy(item => Guid.Parse(item.Id))
                .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
            foreach (var source in sources.Where(source => source.Items.Any(item => _conflicts.Contains(Guid.Parse(item.Id)))))
            {
                source.CanSave = false;
                errors.Add(Path.GetFileName(source.Path) + "：事件ID与其他文件冲突，请修复后刷新。");
            }
            RebuildItems();
            LastError = string.Join(Environment.NewLine, errors);
            return errors.Count == 0;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    private List<string> JsonPaths() => Directory.EnumerateFiles(_path, "*", SearchOption.TopDirectoryOnly)
        .Where(path => Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
        .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase).ToList();

    private static Source ReadSource(string path, bool allowMissing = false)
    {
        string? text = allowMissing && !File.Exists(path) ? null : File.ReadAllText(path);
        return new Source(path) { LoadedText = text, Items = text is null ? [] : ScheduleJson.Parse(text), CanSave = true };
    }

    private void RebuildItems()
    {
        _items = [];
        _owners.Clear();
        foreach (var source in _sources)
        foreach (var item in source.Items.Where(item => !_conflicts.Contains(Guid.Parse(item.Id))))
        {
            _items.Add(item);
            _owners.Add(item, source);
        }
    }

    public List<ScheduleItem> GetForDate(DateTime date) => _items
        .Where(item => ScheduleOccurrence.IsOnDate(item, date))
        .OrderBy(item => item.EventTime).ToList();

    public bool Add(ScheduleItem item)
    {
        if (_sources.Count == 0) { LastError = "请先成功读取日程文件。"; return false; }
        var source = _sources[0];
        item.Id = Guid.NewGuid().ToString();
        return Commit(source, () => source.Items.Add(item), () => source.Items.Remove(item));
    }

    public bool Update(ScheduleItem existing, ScheduleItem replacement)
    {
        if (!_owners.TryGetValue(existing, out var source)) return false;
        int index = source.Items.IndexOf(existing);
        replacement.Id = existing.Id;
        replacement.LastRemindedOccurrenceDate = ScheduleOccurrence.SameReminderIdentity(existing, replacement)
            ? existing.LastRemindedOccurrenceDate : null;
        return Commit(source, () => source.Items[index] = replacement, () => source.Items[index] = existing);
    }

    public bool Delete(ScheduleItem item)
    {
        if (!_owners.TryGetValue(item, out var source)) return false;
        int index = source.Items.IndexOf(item);
        return Commit(source, () => source.Items.RemoveAt(index), () => source.Items.Insert(index, item));
    }

    private bool Commit(Source source, Action apply, Action rollback)
    {
        apply();
        if (SaveSource(source)) { RebuildItems(); return true; }
        rollback();
        RebuildItems();
        return false;
    }

    public bool Save(ScheduleItem item)
    {
        if (_owners.TryGetValue(item, out var source)) return SaveSource(source);
        LastError = "找不到事件所属文件，请刷新后重试。";
        return false;
    }

    // Retained for explicit single-file tools; production changes save their owning file.
    public bool Save()
    {
        if (_sources.Count != 1) { LastError = "请指定事件所属文件保存。"; return false; }
        return SaveSource(_sources[0]);
    }

    private bool SaveSource(Source source)
    {
        string file = source.Path;
        string temp = file + ".tmp";
        bool createdTemp = false;
        try
        {
            if (!source.CanSave) throw new InvalidOperationException("文件尚未成功读取或存在冲突，禁止覆盖保存。请检查文件并刷新。");
            string? current = File.Exists(file) ? File.ReadAllText(file) : null;
            if (current != source.LoadedText)
            {
                source.CanSave = false;
                throw new IOException("日程文件已被其他程序修改，请刷新后重试。");
            }
            string text = ScheduleJson.Serialize(source.Items);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            {
                createdTemp = true;
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(text);
            }
            if (File.Exists(file)) File.Replace(temp, file, file + ".bak");
            else File.Move(temp, file);
            source.LoadedText = text;
            LastError = "";
            return true;
        }
        catch (Exception ex)
        {
            LastError = Path.GetFileName(file) + "：" + ex.Message;
            try { if (createdTemp && File.Exists(temp)) File.Delete(temp); }
            catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException) { }
            return false;
        }
    }
}

// Calendar markers, daily lists and reminders share this exact occurrence predicate.
internal static class ScheduleOccurrence
{
    public static bool IsOnDate(ScheduleItem item, DateTime date)
    {
        date = date.Date;
        DateTime start = item.StartDate.ToSolar(item.Calendar);
        if (date < start || item.RepeatCount < 1) return false;
        int days = (date - start).Days;
        switch (item.Recurrence)
        {
            case RepeatPeriod.None: return date == start;
            case RepeatPeriod.Daily: return days < item.RepeatCount;
            case RepeatPeriod.Weekly: return days % 7 == 0 && days / 7 < item.RepeatCount;
        }
        int count = 0;
        if (item.Calendar == CalendarKind.Solar)
        {
            if (date.Day != item.StartDate.Day ||
                item.Recurrence == RepeatPeriod.Yearly && date.Month != item.StartDate.Month) return false;
            int target = date.Year * 12 + date.Month - 1;
            int step = item.Recurrence == RepeatPeriod.Monthly ? 1 : 12;
            for (int month = start.Year * 12 + start.Month - 1; month <= target; month += step)
                if (DateTime.DaysInMonth(month / 12, month % 12 + 1) >= item.StartDate.Day && ++count > item.RepeatCount)
                    return false;
            return true;
        }

        Lunar.LunarDate lunar;
        try { lunar = Lunar.FromSolar(date); }
        catch (ArgumentOutOfRangeException) { return false; }
        if (lunar.Day != item.StartDate.Day || item.Recurrence == RepeatPeriod.Yearly &&
            (lunar.Month != item.StartDate.Month || lunar.IsLeap != item.StartDate.IsLeapMonth)) return false;
        int firstMonth = item.StartDate.Year * 24 + item.StartDate.Month * 2 + (item.StartDate.IsLeapMonth ? 1 : 0);
        int lastMonth = lunar.Year * 24 + lunar.Month * 2 + (lunar.IsLeap ? 1 : 0);
        // Count real lunar months in order, including the leap month immediately after its ordinary month.
        for (int year = item.StartDate.Year; year <= lunar.Year; year++)
        for (int month = 1; month <= 12; month++)
        for (int leap = 0; leap <= (Lunar.LeapMonthOf(year) == month ? 1 : 0); leap++)
        {
            int ordinal = year * 24 + month * 2 + leap;
            if (ordinal < firstMonth || ordinal > lastMonth || item.Recurrence == RepeatPeriod.Yearly &&
                (month != item.StartDate.Month || (leap == 1) != item.StartDate.IsLeapMonth)) continue;
            int length = leap == 1 ? Lunar.DaysInLeapMonth(year, month) : Lunar.DaysInLunarMonth(year, month);
            if (length >= item.StartDate.Day && ++count > item.RepeatCount) return false;
        }
        return true;
    }

    public static bool SameReminderIdentity(ScheduleItem a, ScheduleItem b) =>
        a.Calendar == b.Calendar && a.StartDate == b.StartDate && a.Recurrence == b.Recurrence &&
        a.EventTime == b.EventTime && a.RemindingMinutes == b.RemindingMinutes &&
        (a.LastRemindedOccurrenceDate is not DateOnly previous || IsOnDate(b, previous.ToDateTime(TimeOnly.MinValue)));
}

internal static class ScheduleJson
{
    private sealed class Document
    {
        [JsonRequired] public int SchemaVersion { get; set; }
        [JsonRequired] public List<ScheduleItem> Events { get; set; } = [];
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new JsonStringEnumConverter<CalendarKind>(JsonNamingPolicy.CamelCase, false),
            new JsonStringEnumConverter<RepeatPeriod>(JsonNamingPolicy.CamelCase, false),
            new MinuteTimeConverter()
        }
    };

    public static List<ScheduleItem> Parse(string text)
    {
        using var parsed = JsonDocument.Parse(text);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object ||
            !parsed.RootElement.TryGetProperty("schemaVersion", out var version) ||
            version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out int number) || number != 2)
            throw new FormatException("仅支持schemaVersion为2的新日程JSON格式；旧格式不兼容，文件未修改。");
        if (parsed.RootElement.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in events.EnumerateArray())
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("location", out _))
                    throw new FormatException("不支持旧 location 字段，请使用 note；文件未修改。");
        }
        var document = JsonSerializer.Deserialize<Document>(text, Options)
            ?? throw new FormatException("日程文件不能为空。");
        ValidateAll(document.Events);
        return document.Events;
    }

    public static string Serialize(IEnumerable<ScheduleItem> items)
    {
        var list = items.ToList();
        ValidateAll(list);
        return JsonSerializer.Serialize(new Document { SchemaVersion = 2, Events = list }, Options) + Environment.NewLine;
    }

    private static void ValidateAll(List<ScheduleItem>? items)
    {
        if (items is null) throw new FormatException("events必须是数组。");
        var ids = new HashSet<Guid>();
        foreach (var item in items)
        {
            Validate(item);
            if (!ids.Add(Guid.Parse(item.Id))) throw new FormatException("日程ID重复。");
        }
    }

    internal static void Validate(ScheduleItem? item)
    {
        if (item is null) throw new FormatException("日程不能为null。");
        if (!Guid.TryParse(item.Id, out var id) || id == Guid.Empty) throw new FormatException("日程ID必须是有效的非空GUID。");
        if (string.IsNullOrWhiteSpace(item.EventName)) throw new FormatException("事件名称不能为空。");
        if (item.Note is null) throw new FormatException("备注必须是字符串。");
        if (!Enum.IsDefined(item.Calendar) || !Enum.IsDefined(item.Recurrence)) throw new FormatException("历法或周期无效。");
        if (item.RepeatCount is < 1 or > 999 || item.Recurrence == RepeatPeriod.None && item.RepeatCount != 1)
            throw new FormatException("重复次数必须是1～999的整数，非周期日程必须为1。");
        if (item.StartDate is null) throw new FormatException("起始日期不能为空。");
        _ = item.StartDate.ToSolar(item.Calendar);
        if (!item.HasTime || item.EventTime!.Value.Second != 0 || item.EventTime.Value.Ticks % TimeSpan.TicksPerMinute != 0)
            throw new FormatException("时间必须精确到分钟。");
        if (item.RemindingMinutes is < 0 or > 99) throw new FormatException("提前提醒必须是0～99分钟。");
        if (item.LastRemindedOccurrenceDate is DateOnly occurrence &&
            !ScheduleOccurrence.IsOnDate(item, occurrence.ToDateTime(TimeOnly.MinValue)))
            throw new FormatException("提醒记录必须对应事件的实际发生日期。");
    }

    private sealed class MinuteTimeConverter : JsonConverter<TimeOnly>
    {
        public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String ||
                !TimeOnly.TryParseExact(reader.GetString(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
                throw new JsonException("时间必须采用HH:mm格式。");
            return value;
        }
        public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("HH:mm", CultureInfo.InvariantCulture));
    }
}
