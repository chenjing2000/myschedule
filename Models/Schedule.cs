using System.Globalization;
using System.Text.Json.Serialization;

namespace OneSchedule;

public enum CalendarKind { Solar, Lunar }
public enum RepeatPeriod { None, Daily, Weekly, Monthly, Yearly }

public sealed record ScheduleDate
{
    [JsonRequired] public int Year { get; init; }
    [JsonRequired] public int Month { get; init; }
    [JsonRequired] public int Day { get; init; }
    [JsonRequired] public bool IsLeapMonth { get; init; }

    public DateTime ToSolar(CalendarKind calendar)
    {
        if (Year < Lunar.MinYear || Year > Lunar.MaxYear)
            throw new ArgumentException("年份必须在1901～2100之间。");
        if (calendar == CalendarKind.Lunar) return Lunar.ToSolar(Year, Month, Day, IsLeapMonth);
        if (IsLeapMonth) throw new ArgumentException("阳历日期不能设置闰月。");
        return new DateTime(Year, Month, Day);
    }
}

public sealed class ScheduleItem
{
    [JsonRequired] public string Id { get; set; } = "";
    [JsonRequired, JsonPropertyName("name")] public string EventName { get; set; } = "";
    [JsonRequired] public string Location { get; set; } = "";
    [JsonRequired] public CalendarKind Calendar { get; set; }
    [JsonRequired] public ScheduleDate StartDate { get; set; } = new();
    [JsonRequired, JsonPropertyName("time")] public TimeOnly? EventTime { get; set; }
    [JsonRequired] public RepeatPeriod Recurrence { get; set; }
    [JsonRequired] public int RepeatCount { get; set; } = 1;

    public static int DefaultRepeatCount(RepeatPeriod period) => period switch
    {
        RepeatPeriod.None => 1,
        RepeatPeriod.Yearly => 100,
        _ => 2
    };
    [JsonRequired, JsonPropertyName("reminderMinutes")] public int RemindingMinutes { get; set; } = 0;
    [JsonRequired] public DateOnly? LastRemindedOccurrenceDate { get; set; }

    [JsonIgnore] public string Time => EventTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
    [JsonIgnore] public bool HasTime => EventTime.HasValue;
}
