using System.Diagnostics;
using System.Globalization;

if (args.Length == 0)
    return 2;

if (args[0] == "--hold")
{
    Thread.Sleep(TimeSpan.FromMinutes(5));
    return 0;
}

if ((args[0] is "--exit-with-child" or "--spawn-child-and-wait") && args.Length > 1)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = Environment.ProcessPath!,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    startInfo.ArgumentList.Add("--hold");
    using var child = Process.Start(startInfo);
    if (child is null)
        return 3;
    File.WriteAllText(args[1], child.Id.ToString(CultureInfo.InvariantCulture));
    if (args[0] == "--spawn-child-and-wait")
        Thread.Sleep(TimeSpan.FromMinutes(5));
    return 0;
}

if (args[0] == "--wait")
{
    Thread.Sleep(TimeSpan.FromMinutes(5));
    return 0;
}

return 4;
