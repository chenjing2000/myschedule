using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using OneSchedule;

// Explicit developer utility. The application itself only accepts the current schema.
internal static class DataUpgrade
{
    internal static string ConvertVersion1(string text) => ConvertLegacy(text,1);
    internal static string ConvertVersion2(string text) => ConvertLegacy(text,2);

    private static string ConvertLegacy(string text, int expectedVersion)
    {
        var root=JsonNode.Parse(text)?.AsObject() ?? throw new FormatException("日程文件不能为空。");
        if (root["schemaVersion"]?.GetValue<int>()!=expectedVersion) throw new FormatException("升级工具版本不匹配。");
        if (root.Any(pair => pair.Key is not ("schemaVersion" or "events"))) throw new FormatException("未知根字段。");
        var events=root["events"]?.AsArray() ?? throw new FormatException("events必须是数组。");
        var records=new List<DateOnly?>();
        var expected=new HashSet<string> { "id","name","note","calendar","startDate","time","recurrence","reminderMinutes","lastRemindedOccurrenceDate" };
        if (expectedVersion==2) expected.Add("repeatCount");
        foreach (var node in events)
        {
            var item=node?.AsObject() ?? throw new FormatException("日程不能为null。");
            if (item.ContainsKey("location")) throw new FormatException("旧location字段不兼容，请使用note。");
            if (!expected.SetEquals(item.Select(pair => pair.Key))) throw new FormatException("旧版本字段缺失或未知，未升级。");
            records.Add(item["lastRemindedOccurrenceDate"] is { } value
                ? DateOnly.ParseExact(value.GetValue<string>(),"yyyy-MM-dd",CultureInfo.InvariantCulture) : null);
            if (!Enum.TryParse<RepeatPeriod>(item["recurrence"]?.GetValue<string>(),true,out var period) || !Enum.IsDefined(period))
                throw new FormatException("周期无效。");
            item["recurrence"]=period.ToString().ToLowerInvariant();
            if (expectedVersion==1) item["repeatCount"]=ScheduleItem.DefaultRepeatCount(period);
            item.Remove("lastRemindedOccurrenceDate");
            item["reminderCount"]=3; item["reminderIntervalMinutes"]=3; item["reminderDurationMinutes"]=1;
            item["completedReminderDates"]=new JsonArray();
        }
        root["schemaVersion"]=3;
        var items=ScheduleJson.Parse(root.ToJsonString());
        for (int i=0;i<items.Count;i++)
        {
            if (records[i] is not DateOnly record) continue;
            if (ScheduleOccurrence.IsOnDate(items[i],record.ToDateTime(TimeOnly.MinValue))) items[i].CompletedReminderDates.Add(record);
            else if (expectedVersion==2) throw new FormatException("旧提醒记录不是实际发生日期，未升级。");
        }
        return ScheduleJson.Serialize(items);
    }

    internal static int UpgradeFile(string path)
    {
        string? temp=null; bool ownsTemp=false;
        try
        {
            path=Path.GetFullPath(path);
            string original=File.ReadAllText(path);
            int version=JsonNode.Parse(original)?["schemaVersion"]?.GetValue<int>() ?? 0;
            if (version==3) { _=ScheduleJson.Parse(original); Console.WriteLine("PASS: already version3; file unchanged"); return 0; }
            string converted=version switch { 1=>ConvertVersion1(original), 2=>ConvertVersion2(original), _=>throw new FormatException("仅支持显式升级版本1或2。") };
            string backup=path+".schema-v"+version+"-"+Guid.NewGuid().ToString("N")[..8]+".bak";
            temp=path+".upgrade.tmp";
            using (var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write))
            {
                ownsTemp=true;
                using var writer=new StreamWriter(stream,new UTF8Encoding(false));
                writer.Write(converted);
            }
            if (File.ReadAllText(path)!=original) throw new IOException("日程已被外部修改，未替换。");
            File.Replace(temp,path,backup);
            Console.WriteLine("PASS: upgraded "+ScheduleJson.Parse(converted).Count+" events to version3; backup: "+backup);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        finally { if (ownsTemp && temp is not null && File.Exists(temp)) File.Delete(temp); }
    }
}
