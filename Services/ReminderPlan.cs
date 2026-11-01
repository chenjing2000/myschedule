namespace OneSchedule;

internal sealed record ReminderOccurrence(ScheduleItem Item, DateOnly Date, int Round, DateTime StartsAt, DateTime EndsAt);

// One schedule determines both the live alarm windows and the calendar marker's pending state.
internal static class ReminderPlan
{
    internal static DateTime FirstStart(ScheduleItem item, DateOnly date) =>
        date.ToDateTime(item.EventTime!.Value).AddMinutes(-item.RemindingMinutes);

    internal static DateTime End(ScheduleItem item, DateOnly date) => FirstStart(item,date)
        .AddMinutes((item.ReminderCount-1)*item.ReminderIntervalMinutes + item.ReminderDurationMinutes);

    internal static bool IsComplete(ScheduleItem item, DateOnly date, DateTime now) =>
        item.CompletedReminderDates.Contains(date) || item.HasTime && now >= End(item,date);

    internal static IEnumerable<ReminderOccurrence> ActiveAt(IEnumerable<ScheduleItem> items, DateTime now)
    {
        foreach (var item in items)
        {
            if (!item.HasTime) continue;
            int span=(item.ReminderCount-1)*item.ReminderIntervalMinutes + item.ReminderDurationMinutes;
            var first=now.AddMinutes(item.RemindingMinutes-span).Date;
            var last=now.AddMinutes(item.RemindingMinutes).Date;
            for (var date=first; date<=last; date=date.AddDays(1))
            {
                var occurrence=DateOnly.FromDateTime(date);
                if (!ScheduleOccurrence.IsOnDate(item,date) || IsComplete(item,occurrence,now)) continue;
                var start=FirstStart(item,occurrence);
                if (now<start) continue;
                int round=item.ReminderCount==1 ? 0 : (int)((now-start).TotalMinutes/item.ReminderIntervalMinutes);
                if (round>=item.ReminderCount) continue;
                var roundStart=start.AddMinutes(round*item.ReminderIntervalMinutes);
                var end=roundStart.AddMinutes(item.ReminderDurationMinutes);
                if (now<end) yield return new ReminderOccurrence(item,occurrence,round+1,roundStart,end);
            }
        }
    }
}
