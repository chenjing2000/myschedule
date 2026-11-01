using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using OneSchedule;

internal static class ReminderSeriesChecks
{
    private static readonly DateOnly Date=new(2026,11,2);
    private static readonly DateTime Start=new(2026,11,2,8,30,0);
    public static void Run()
    {
        WindowsAndRestart(); ValidationAndUpgrade(); ServiceLifecycle();
        Console.WriteLine("PASS: reminder series windows, restart, shared alarms, cancellation, rollback and explicit data upgrade");
    }
    private static ScheduleItem Item(string name="series") => new()
    {
        Id=Guid.NewGuid().ToString(), EventName=name, Calendar=CalendarKind.Solar,
        StartDate=new ScheduleDate { Year=2026,Month=11,Day=2 }, EventTime=new TimeOnly(9,0),
        RemindingMinutes=30, ReminderCount=3, ReminderIntervalMinutes=3, ReminderDurationMinutes=1
    };
    private static void WindowsAndRestart()
    {
        var item=Item();
        foreach (var pair in new[] { (0,true),(59,true),(60,false),(179,false),(180,true),(239,true),(240,false),(360,true),(419,true),(420,false) })
            Require((ReminderService.FindDue([item],Start.AddSeconds(pair.Item1),out _)==item)==pair.Item2,"round boundary "+pair.Item1);
        var restored=ScheduleJson.Parse(ScheduleJson.Serialize([item])).Single();
        var round=ReminderPlan.ActiveAt([restored],Start.AddMinutes(3.5)).Single();
        Require(round.Round==2 && round.EndsAt==Start.AddMinutes(4),"restart runs only the remaining half minute");
        Require(!ReminderPlan.ActiveAt([restored],Start.AddMinutes(5)).Any(),"restart in gap does not replay missed rounds");
        Require(ReminderPlan.ActiveAt([restored],Start.AddMinutes(6)).Single().Round==3,"future round survives restart");
        restored.CompletedReminderDates=[Date];
        Require(!ReminderPlan.ActiveAt([restored],Start.AddMinutes(6)).Any(),"acknowledged occurrence never restarts");
        restored.Recurrence=RepeatPeriod.Daily; restored.RepeatCount=2;
        Require(ReminderPlan.ActiveAt([restored],Start.AddDays(1)).Single().Date==Date.AddDays(1),"next daily occurrence remains independent");
        item.EventTime=new TimeOnly(0,5); item.StartDate=new ScheduleDate { Year=2027,Month=1,Day=1 };
        Require(ReminderPlan.ActiveAt([item],new DateTime(2026,12,31,23,35,30)).Single().Date==new DateOnly(2027,1,1),"early reminder crosses midnight and year");
        item=Item(); item.Recurrence=RepeatPeriod.Daily; item.RepeatCount=99;
        item.RemindingMinutes=0; item.ReminderCount=99; item.ReminderIntervalMinutes=60;
        var overlap=ReminderPlan.ActiveAt([item],new DateTime(2026,11,6,9,0,0)).ToList();
        Require(overlap.Count==5 && overlap.Select(o=>o.Date).Distinct().Count()==5,"long daily series can overlap without merging occurrence dates");
        item.CompletedReminderDates=[new DateOnly(2026,11,4)];
        Require(ReminderPlan.ActiveAt([item],new DateTime(2026,11,6,9,0,0)).Count()==4,"completion affects only one overlapping occurrence");
        item=Item(); item.ReminderDurationMinutes=3;
        Require(ReminderPlan.ActiveAt([item],Start.AddMinutes(3)).Single().Round==2,"touching round boundaries are not double-counted");
    }
    private static void ValidationAndUpgrade()
    {
        var defaults=new ScheduleItem();
        Require(defaults.RemindingMinutes==30 && defaults.ReminderCount==3 && defaults.ReminderIntervalMinutes==3 && defaults.ReminderDurationMinutes==1,"model defaults 30/3/3/1");
        foreach (var change in new Action<ScheduleItem>[] { i=>i.ReminderCount=0,i=>i.ReminderCount=100,i=>i.ReminderIntervalMinutes=0,i=>i.ReminderDurationMinutes=0,i=>i.ReminderDurationMinutes=4,i=>i.CompletedReminderDates=[Date,Date],i=>i.CompletedReminderDates=[Date.AddDays(-1)] })
        {
            var invalid=Item(); change(invalid); bool rejected=false;
            try { _=ScheduleJson.Serialize([invalid]); } catch (FormatException) { rejected=true; }
            Require(rejected,"invalid configuration rejected");
        }
        var once=Item(); once.ReminderCount=1; once.ReminderDurationMinutes=99; once.ReminderIntervalMinutes=1;
        _=ScheduleJson.Serialize([once]);
        Require(ReminderPlan.ActiveAt([once],Start.AddMinutes(98)).Single().Round==1,"single long round ignores interval through its full duration");
        Require(!ReminderPlan.ActiveAt([once],Start.AddMinutes(99)).Any(),"single long round stops exactly at duration");
        var legacy=JsonNode.Parse(ScheduleJson.Serialize([Item()]))!.AsObject();
        legacy["schemaVersion"]=2;
        var old=legacy["events"]![0]!.AsObject();
        foreach (var field in new[] { "reminderCount","reminderIntervalMinutes","reminderDurationMinutes","completedReminderDates" }) old.Remove(field);
        old["lastRemindedOccurrenceDate"]="2026-11-02"; old["reminderMinutes"]=37;
        string original=legacy.ToJsonString();
        bool rejectedOld=false; try { ScheduleJson.Parse(original); } catch (FormatException) { rejectedOld=true; }
        Require(rejectedOld,"runtime rejects version2 instead of migrating silently");
        var upgraded=ScheduleJson.Parse(DataUpgrade.ConvertVersion2(original)).Single();
        Require(upgraded.Id==old["id"]!.GetValue<string>() && upgraded.RemindingMinutes==37 && upgraded.ReminderCount==3 && upgraded.CompletedReminderDates.SequenceEqual(new[] { Date }),"upgrade preserves identity, original lead and completed occurrence");
        string folder=Path.Combine(AppContext.BaseDirectory,"series-upgrade",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            string file=Path.Combine(folder,"legacy.json"); File.WriteAllText(file,original); var bytes=File.ReadAllBytes(file);
            Require(DataUpgrade.UpgradeFile(file)==0,"explicit version2 upgrade succeeds");
            Require(File.ReadAllBytes(Directory.GetFiles(folder,"*.bak").Single()).SequenceEqual(bytes),"upgrade backup preserves original bytes");
            string converted=File.ReadAllText(file);
            Require(DataUpgrade.UpgradeFile(file)==0 && File.ReadAllText(file)==converted && Directory.GetFiles(folder,"*.bak").Length==1,"new schema upgrade is idempotent");
            File.WriteAllText(file,original); File.WriteAllText(file+".upgrade.tmp","owned elsewhere");
            Require(DataUpgrade.UpgradeFile(file)==1 && File.ReadAllText(file)==original && File.ReadAllText(file+".upgrade.tmp")=="owned elsewhere","upgrade never overwrites another temporary file");
        }
        finally { Directory.Delete(folder,true); }
        var originalItem=Item(); originalItem.CompletedReminderDates=[Date];
        foreach (var change in new Action<ScheduleItem>[] { i=>i.ReminderCount=4,i=>i.ReminderIntervalMinutes=4,i=>i.ReminderDurationMinutes=2 })
        {
            var edited=Item(); change(edited); ScheduleOccurrence.CopyReminderState(originalItem,edited);
            Require(edited.CompletedReminderDates.Count==0,"reminder configuration edit resets completion");
        }
        var renamed=Item("renamed"); ScheduleOccurrence.CopyReminderState(originalItem,renamed);
        renamed.CompletedReminderDates.Clear();
        Require(originalItem.CompletedReminderDates.Count==1,"editing clones completion state rather than sharing its list");
    }
    private static void ServiceLifecycle()
    {
        string folder=Path.Combine(AppContext.BaseDirectory,"series-service",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var owner=new Window();
        try
        {
            string file=Path.Combine(folder,"events.json"); var service=new ScheduleService(file);
            Require(service.Reload() && service.Add(Item()),"service fixture saved");
            DateTime now=Start.AddSeconds(10); int stops=0;
            using (var first=new ReminderService(service,owner,_=>{},_=>{},null,()=>now,false))
            {
                first.ProcessAt(now); Require(first.IsAlertActive,"first round is active");
            }
            Require(service.Items.Single().CompletedReminderDates.Count==0,"exit does not cancel the series");
            Require(service.Reload(),"restart reloads state");
            using (var restored=new ReminderService(service,owner,_=>{},_=>{},null,()=>now,false,()=>stops++))
            {
                now=Start.AddSeconds(45); restored.ProcessAt(now);
                Require(restored.ActiveReminders.Single().EndsAt==Start.AddMinutes(1),"restart keeps original round end");
                now=Start.AddMinutes(1); restored.ProcessAt(now); Require(!restored.IsAlertActive,"automatic one-minute stop");
                now=Start.AddMinutes(3); restored.ProcessAt(now); Require(restored.IsAlertActive,"next round starts after quiet interval");
                Require(restored.DismissAlert() && !restored.IsAlertActive,"manual acknowledgement stops current alarm");
                now=Start.AddMinutes(6); restored.ProcessAt(now); Require(!restored.IsAlertActive,"acknowledgement cancels later rounds");
            }
            Require(service.Reload() && service.Items.Single().CompletedReminderDates.Contains(Date),"acknowledgement survives file reload");
            Require(service.Delete(service.Items.Single()),"clear fixture");
            Require(service.Add(Item("refresh")),"observed refresh fixture saved");
            now=Start.AddSeconds(10);
            using (var refreshed=new ReminderService(service,owner,_=>{},_=>{},null,()=>now,false))
            {
                refreshed.ProcessAt(now); now=Start.AddMinutes(1); refreshed.ProcessAt(now);
                Require(service.Reload(),"refresh between rounds");
                now=Start.AddMinutes(8); refreshed.ProcessAt(now);
                Require(service.Items.Single().CompletedReminderDates.Contains(Date),"observed completion survives refresh during quiet interval");
            }
            Require(service.Delete(service.Items.Single()),"clear observed refresh fixture");
            var a=Item("A"); var b=Item("B"); var waiting=Item("waiting"); waiting.EventTime=new TimeOnly(9,2);
            Require(service.Add(a) && service.Add(b) && service.Add(waiting),"multiple events saved");
            now=Start.AddSeconds(10);
            using (var shared=new ReminderService(service,owner,_=>{},_=>{},null,()=>now,false))
            {
                shared.ProcessAt(now); Require(shared.ActiveReminders.Count==2,"concurrent events share an active alarm");
                shared.DismissAlert();
                Require(a.CompletedReminderDates.Contains(Date) && b.CompletedReminderDates.Contains(Date) && waiting.CompletedReminderDates.Count==0,"dismiss current events but not waiting event");
                now=Start.AddMinutes(2); shared.ProcessAt(now); Require(shared.ActiveReminders.Single().Item==waiting,"waiting event still starts later");
                now=Start.AddMinutes(8); shared.ProcessAt(now);
                now=Start.AddMinutes(9); shared.ProcessAt(now);
                Require(!shared.IsAlertActive && waiting.CompletedReminderDates.Contains(Date),"final timeout records completion");
            }
            foreach (var item in service.Items.ToList()) Require(service.Delete(item),"clear completed fixtures");
            var failure=Item(); Require(service.Add(failure),"rollback fixture saved");
            now=Start.AddSeconds(10); int errors=0;
            using (var alarm=new ReminderService(service,owner,_=>{},_=>{},_=>errors++,()=>now,false))
            {
                alarm.ProcessAt(now); string original=File.ReadAllText(file); File.WriteAllText(file,"external edit");
                alarm.DismissAlert();
                Require(!alarm.IsAlertActive && errors>0 && failure.CompletedReminderDates.Count==0 && File.ReadAllText(file)=="external edit","save failure silences locally, rolls back durable state and preserves external source");
                now=Start.AddMinutes(3); alarm.ProcessAt(now); Require(!alarm.IsAlertActive,"failed acknowledgement does not restart local sound");
                File.WriteAllText(file,original); Require(service.Reload(),"refresh restored source"); alarm.ProcessAt(now);
                Require(service.Items.Single().CompletedReminderDates.Contains(Date),"pending acknowledgement retries once source is restored and refreshed");
            }
        }
        finally { owner.Close(); Directory.Delete(folder,true); }
    }
    private static void Require(bool condition,string message) { if (!condition) throw new Exception(message); }
}
