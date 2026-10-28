using System.IO;
using System.Text.Json;
using OneSchedule;

internal static class NoteChecks
{
    public static void Run()
    {
        var item=new ScheduleItem { Id=Guid.NewGuid().ToString(), EventName="event", Note="note content", Calendar=CalendarKind.Solar,
            StartDate=new ScheduleDate { Year=2026,Month=10,Day=28 }, EventTime=new TimeOnly(9,30) };
        string canonical=ScheduleJson.Serialize([item]);
        Require(canonical.Contains("\"note\"") && !canonical.Contains("\"location\""),"serialize only note");
        Require(ScheduleJson.Parse(canonical).Single().Note==item.Note,"roundtrip note");
        string legacy=canonical.Replace("\"note\"", "\"location\"");
        try { ScheduleJson.Parse(legacy); throw new Exception("old location was accepted"); }
        catch (FormatException ex) { Require(ex.Message.Contains("location") && ex.Message.Contains("note"), "legacy field produces a clear error"); }
        Reject(canonical.Replace("\"note\":", "\"location\":\"old\",\"note\":"),"note and location together are rejected");
        string folder=Path.Combine(AppContext.BaseDirectory,"note-fixtures",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            string file=Path.Combine(folder,"old.json");File.WriteAllText(file,legacy);byte[] original=File.ReadAllBytes(file);
            var service=new ScheduleService(file);
            Require(!service.Reload() && !service.CanSave && !service.Add(item),"legacy file cannot be saved over");
            Require(File.ReadAllBytes(file).SequenceEqual(original),"legacy file bytes remain untouched");
            File.WriteAllText(Path.Combine(folder,"valid.json"),canonical);
            var multi=ScheduleService.ForDirectory(folder);
            Require(!multi.Reload() && multi.Items.Count==1 && multi.Items[0].Note==item.Note,"valid independent file remains readable");
            Require(File.ReadAllBytes(file).SequenceEqual(original),"directory scan leaves invalid legacy file unchanged");
        }
        finally { Directory.Delete(folder,true); }
        Console.WriteLine("PASS: strict note-only JSON, rejection and legacy file protection");
    }
    private static void Reject(string text,string message)
    {
        bool rejected=false;try { ScheduleJson.Parse(text); } catch(Exception ex) when(ex is JsonException or FormatException or ArgumentException) { rejected=true; }
        Require(rejected,message);
    }
    private static void Require(bool ok,string message) { if(!ok) throw new Exception(message); }
}
