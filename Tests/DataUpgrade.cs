using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using OneSchedule;

// Explicit developer utility; the application parser never silently upgrades a file.
internal static class DataUpgrade
{
    internal static string ConvertVersion1(string text)
    {
        var root = JsonNode.Parse(text)?.AsObject() ?? throw new FormatException("日程文件不能为空。");
        if (root["schemaVersion"]?.GetValue<int>() != 1) throw new FormatException("升级工具只接受版本1。");
        var events = root["events"]?.AsArray() ?? throw new FormatException("events必须是数组。");
        var reminders = new List<DateOnly?>();
        foreach (var node in events)
        {
            var item = node?.AsObject() ?? throw new FormatException("日程不能为null。");
            if (item.ContainsKey("repeatCount")) throw new FormatException("版本1不应包含repeatCount。");
            DateOnly? record = item["lastRemindedOccurrenceDate"] is { } value
                ? DateOnly.ParseExact(value.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
            reminders.Add(record);
            string period = item["recurrence"]?.GetValue<string>() ?? throw new FormatException("缺少周期。");
            if (!Enum.TryParse<RepeatPeriod>(period, true, out var recurrence) || !Enum.IsDefined(recurrence))
                throw new FormatException("周期无效。");
            item["repeatCount"] = ScheduleItem.DefaultRepeatCount(recurrence);
            item["lastRemindedOccurrenceDate"] = null;
        }
        root["schemaVersion"] = 2;
        var items = ScheduleJson.Parse(root.ToJsonString());
        for (int i = 0; i < items.Count; i++)
            if (reminders[i] is DateOnly record && ScheduleOccurrence.IsOnDate(items[i], record.ToDateTime(TimeOnly.MinValue)))
                items[i].LastRemindedOccurrenceDate = record;
        return ScheduleJson.Serialize(items);
    }

    internal static int UpgradeFile(string path)
    {
        try
        {
            path = Path.GetFullPath(path);
            string original = File.ReadAllText(path);
            if (JsonNode.Parse(original)?["schemaVersion"]?.GetValue<int>() == 2)
            {
                _ = ScheduleJson.Parse(original);
                Console.WriteLine("PASS: already version2; file unchanged");
                return 0;
            }
            string converted = ConvertVersion1(original);
            string backup = path + ".schema-v1-" + Guid.NewGuid().ToString("N")[..8] + ".bak";
            string temp = path + ".upgrade.tmp";
            if (File.Exists(temp)) throw new IOException("升级临时文件已存在，未修改日程。");
            try
            {
                File.WriteAllText(temp, converted, new UTF8Encoding(false));
                if (File.ReadAllText(path) != original) throw new IOException("日程已被其他程序修改，未替换。");
                // Replacement preserves the original bytes in a separate backup, never the rolling .bak.
                File.Replace(temp, path, backup);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            Console.WriteLine("PASS: upgraded " + ScheduleJson.Parse(converted).Count + " events to version2");
            Console.WriteLine("Original backup: " + backup);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
}
