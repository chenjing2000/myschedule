using System.IO;
using OneSchedule;

internal static class MultiFileChecks
{
    public static void Run()
    {
        string root=Path.Combine(AppContext.BaseDirectory,"multi-fixtures",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string data=Path.Combine(root,"data");
            var service=ScheduleService.ForDirectory(data);
            Require(service.Reload() && File.Exists(Path.Combine(data,"MySchedule.json")),"empty folder initializes valid default document");
            Require(ScheduleJson.Parse(File.ReadAllText(Path.Combine(data,"MySchedule.json"))).Count==0,"initial document contains no events");
            File.Delete(Path.Combine(data,"MySchedule.json"));
            var a=Item("a"); var z=Item("z");
            string first=Path.Combine(data,"A.json"),last=Path.Combine(data,"z.JSON");
            File.WriteAllText(first,ScheduleJson.Serialize([a]));File.WriteAllText(last,ScheduleJson.Serialize([z]));
            File.WriteAllText(Path.Combine(data,"ignored.json.bak"),"invalid");
            Directory.CreateDirectory(Path.Combine(data,"nested"));File.WriteAllText(Path.Combine(data,"nested","nested.json"),"invalid");
            Require(service.Reload() && service.Items.Count==2 && !File.Exists(Path.Combine(data,"MySchedule.json")),"scan includes uppercase extension but excludes backup and nested JSON");
            Require(service.GetForDate(new DateTime(2026,10,27)).Count==2,"merged events participate in calendar occurrence rules");
            string untouched=File.ReadAllText(last);
            Require(service.Add(Item("new")) && ScheduleJson.Parse(File.ReadAllText(first)).Count==2 && File.ReadAllText(last)==untouched,"add targets first filename only");
            a=service.Items.Single(x=>x.EventName=="a");z=service.Items.Single(x=>x.EventName=="z");
            string firstText=File.ReadAllText(first);
            var replacement=Item("renamed");
            Require(service.Update(z,replacement) && File.ReadAllText(first)==firstText && ScheduleJson.Parse(File.ReadAllText(last)).Single().EventName=="renamed","edit writes only source file");
            Require(File.Exists(last+".bak"),"source edit creates source backup");
            replacement.LastRemindedOccurrenceDate=new DateOnly(2026,10,27);
            Require(service.Save(replacement) && File.ReadAllText(first)==firstText,"reminder save targets source file");
            Require(service.Delete(replacement) && ScheduleJson.Parse(File.ReadAllText(last)).Count==0,"delete writes source file");
            File.WriteAllText(last,"broken");
            Require(!service.Reload() && service.Items.Count==2 && service.LastError.Contains("z.JSON"),"malformed source reported while valid sources remain visible");
            Require(service.Add(Item("still valid")) && File.ReadAllText(last)=="broken","healthy source remains writable without corrupt overwrite");
            File.WriteAllText(first,"bad first");File.WriteAllText(last,ScheduleJson.Serialize([Item("healthy")]));
            Require(!service.Reload() && service.Items.Count==1 && !service.Add(Item("blocked")),"corrupt first file cannot silently redirect additions");
            var healthy=service.Items.Single();
            Require(service.Delete(healthy) && File.ReadAllText(first)=="bad first","healthy file delete remains independent of damaged first file");
            var shared=Item("collision");
            File.WriteAllText(first,ScheduleJson.Serialize([shared,Item("independent a")]));
            File.WriteAllText(last,ScheduleJson.Serialize([shared,Item("independent z")]));
            Require(!service.Reload() && service.LastError.Contains("ID") && service.Items.Count==2,"duplicate IDs are reported and conflicting events excluded");
            Require(!service.Add(Item("blocked collision")),"conflicting source cannot be overwritten");
            File.WriteAllText(last,ScheduleJson.Serialize([Item("recovered")]));
            Require(service.Reload(),"refresh recovers resolved source conflict");
            var original=service.Items.Single(x=>x.EventName=="recovered");
            File.WriteAllText(last,ScheduleJson.Serialize([Item("external")]));string external=File.ReadAllText(last);
            Require(!service.Delete(original) && service.Items.Contains(original) && File.ReadAllText(last)==external,"external edit blocks and rolls back targeted delete");
            File.Delete(first);File.Delete(last);
            Require(service.Reload() && service.Items.Count==0 && File.Exists(Path.Combine(data,"MySchedule.json")),"refresh recreates default when no top-level JSON remains");
            Console.WriteLine("PASS: multi-file initialization, ownership, partial errors, conflicts and rollback");
        }
        finally { Directory.Delete(root,true); }
    }
    private static ScheduleItem Item(string name)=>new() { Id=Guid.NewGuid().ToString(),EventName=name,Calendar=CalendarKind.Solar,StartDate=new ScheduleDate { Year=2026,Month=10,Day=27 },EventTime=new TimeOnly(9,0) };
    private static void Require(bool value,string message) { if(!value)throw new Exception(message); }
}
