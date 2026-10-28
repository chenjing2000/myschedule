using System.IO;
using System.Text.Json;
using OneSchedule;

internal static class NoteChecks
{
    public static void Run()
    {
        var item=new ScheduleItem { Id=Guid.NewGuid().ToString(), EventName="event", Note="old content", Calendar=CalendarKind.Solar,
            StartDate=new ScheduleDate { Year=2026,Month=10,Day=28 }, EventTime=new TimeOnly(9,30) };
        string canonical=ScheduleJson.Serialize([item]);
        Require(canonical.Contains("\"note\"") && !canonical.Contains("\"location\""),"serialize only note");
        string legacy=canonical.Replace("\"note\"", "\"location\"");
        Require(ScheduleJson.Parse(legacy).Single().Note==item.Note,"read old location without losing text");
        Require(ScheduleJson.Parse(canonical).Single().Note==item.Note,"roundtrip note");
        string both=canonical.Replace("\"note\":", "\"location\":\"conflict\",\"note\":");
        bool rejected=false;try { ScheduleJson.Parse(both); } catch (Exception ex) when(ex is FormatException or JsonException) { rejected=true; }
        Require(rejected,"conflicting old and new fields are rejected");
        string folder=Path.Combine(AppContext.BaseDirectory,"note-fixtures",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            string file=Path.Combine(folder,"events.json");File.WriteAllText(file,legacy);
            var original=File.ReadAllBytes(file);
            Require(DataUpgrade.UpgradeNotes(file)==0,"explicit field migration succeeds");
            var backup=Directory.GetFiles(folder,"*.note-v2-*.bak").Single();
            Require(File.ReadAllBytes(backup).SequenceEqual(original),"original bytes preserved in separate backup");
            string converted=File.ReadAllText(file);
            Require(converted.Contains("\"note\"") && !converted.Contains("\"location\"") && ScheduleJson.Parse(converted).Single().Note==item.Note,"converted data preserves note");
            Require(DataUpgrade.UpgradeNotes(file)==0 && Directory.GetFiles(folder,"*.note-v2-*.bak").Length==1,"repeat migration is idempotent");
            File.WriteAllText(file,both);string invalid=File.ReadAllText(file);
            Require(DataUpgrade.UpgradeNotes(file)!=0 && File.ReadAllText(file)==invalid,"ambiguous input is never overwritten");
        }
        finally { Directory.Delete(folder,true); }
        Console.WriteLine("PASS: note serialization, legacy compatibility and safe migration");
    }
    private static void Require(bool ok,string message) { if(!ok) throw new Exception(message); }
}
