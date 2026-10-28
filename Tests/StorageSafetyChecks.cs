using System.IO;
using OneSchedule;

internal static class StorageSafetyChecks
{
    public static void Run()
    {
        var errors=new List<Exception>();
        foreach(var check in new Action<string>[] { GuidIdentity, TemporaryFileOwnership })
        {
            string folder=Path.Combine(AppContext.BaseDirectory,"storage-safety",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            try { check(folder);Console.WriteLine("PASS: storage safety "+check.Method.Name); }
            catch(Exception ex) { errors.Add(ex);Console.WriteLine("FAIL: storage safety "+check.Method.Name+": "+ex.Message); }
            finally { Directory.Delete(folder,true); }
        }
        if(errors.Count>0) throw new AggregateException(errors);
    }
    private static void GuidIdentity(string folder)
    {
        var first=Item("first");var second=Item("second");
        second.Id=Guid.Parse(first.Id).ToString("B").ToUpperInvariant();
        File.WriteAllText(Path.Combine(folder,"a.json"),ScheduleJson.Serialize([first]));
        File.WriteAllText(Path.Combine(folder,"b.json"),ScheduleJson.Serialize([second]));
        var service=ScheduleService.ForDirectory(folder);
        Require(!service.Reload() && service.Items.Count==0 && !service.CanSave && service.LastError.Contains("ID"),
            "equal GUID values in different textual formats must conflict across files");
    }
    private static void TemporaryFileOwnership(string folder)
    {
        string path=Path.Combine(folder,"events.json"),temp=path+".tmp";
        var service=new ScheduleService(path);
        Require(service.Reload() && service.Add(Item("original")),"storage fixture saves");
        string original=File.ReadAllText(path);
        const string leftover="pre-existing temporary content";
        File.WriteAllText(temp,leftover);
        Require(!service.Add(Item("blocked")) && service.Items.Count==1 && File.ReadAllText(path)==original && File.ReadAllText(temp)==leftover,
            "save must not overwrite or delete a pre-existing temporary file");
        File.WriteAllText(path,"external edit");
        Require(!service.Save() && File.ReadAllText(temp)==leftover && File.ReadAllText(path)=="external edit",
            "preflight failure must not clean up another operation's temporary file");
    }
    private static ScheduleItem Item(string name)=>new() { Id=Guid.NewGuid().ToString(),EventName=name,Calendar=CalendarKind.Solar,
        StartDate=new ScheduleDate { Year=2026,Month=10,Day=28 },EventTime=new TimeOnly(9,0) };
    private static void Require(bool ok,string message) { if(!ok) throw new Exception(message); }
}
