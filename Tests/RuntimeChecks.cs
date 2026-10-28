using System.Reflection;
using System.Runtime.Versioning;
using OneSchedule;

internal static class RuntimeChecks
{
    public static void Run()
    {
        if (Environment.Version.Major != 10)
            throw new Exception("Checks must run on .NET 10; actual: " + Environment.Version);
        foreach (var assembly in new[] { typeof(RuntimeChecks).Assembly, typeof(ScheduleItem).Assembly })
        {
            string? target = assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
            if (target != ".NETCoreApp,Version=v10.0")
                throw new Exception(assembly.GetName().Name + " must target .NET 10; actual: " + target);
        }
        Console.WriteLine("PASS: application and checks target .NET 10 and run on .NET " + Environment.Version);
    }
}
