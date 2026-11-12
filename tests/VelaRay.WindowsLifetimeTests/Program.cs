using System.Diagnostics;
using VelaRay.Maui.Services;

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("This regression test requires Windows.");

if (args.Length == 2 && args[0] == "--helper")
{
    // Stand-in for the VelaRay UI host. Its job handle is the ONLY job handle.
    // The parent will terminate this process without running C# finally blocks.
    using var lifetime = new WindowsJobObjectLifetime();
    using var worker = LaunchLongRunningProcess();
    lifetime.Attach(worker);
    File.WriteAllText(args[1], worker.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    Thread.Sleep(Timeout.Infinite);
    return;
}

// 1) An explicit job handle close must immediately terminate the child process.
using (var lifetime = new WindowsJobObjectLifetime())
using (var worker = LaunchLongRunningProcess())
{
    lifetime.Attach(worker);
    lifetime.Dispose();
    if (!worker.WaitForExit(5000))
    {
        worker.Kill(entireProcessTree: true);
        throw new Exception("Job Object did not terminate child after Dispose().");
    }
}
Console.WriteLine("PASS: explicit close terminates the child.");

// 2) Force-kill the hosting process: the OS closes its job handle and the
// child must terminate even though the host never executed its exit handler.
var pidPath = Path.Combine(Path.GetTempPath(), $"velaray-job-test-{Guid.NewGuid():N}.txt");
using var helper = new Process();
helper.StartInfo = new ProcessStartInfo("dotnet")
{
    UseShellExecute = false,
    CreateNoWindow = true
};
helper.StartInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
helper.StartInfo.ArgumentList.Add("--helper");
helper.StartInfo.ArgumentList.Add(pidPath);
var childId = -1;
var helperStarted = false;
try
{
    if (!helper.Start()) throw new Exception("Failed to start crash-test helper.");
    helperStarted = true;
    var deadline = DateTime.UtcNow.AddSeconds(10);
    while (DateTime.UtcNow < deadline)
    {
        if (File.Exists(pidPath) &&
            int.TryParse(File.ReadAllText(pidPath), out var receivedPid))
        {
            childId = receivedPid;
            break;
        }
        if (helper.HasExited)
            throw new Exception($"Crash-test helper exited early (code {helper.ExitCode}).");
        Thread.Sleep(100);
    }
    if (childId <= 0) throw new TimeoutException("Crash-test child PID was not reported.");
    helper.Kill(entireProcessTree: false); // Do NOT let Process.Kill kill the child for us.
    if (!helper.WaitForExit(5000)) throw new TimeoutException("Could not terminate test helper.");

    deadline = DateTime.UtcNow.AddSeconds(5);
    while (DateTime.UtcNow < deadline && IsAlive(childId)) Thread.Sleep(100);
    if (IsAlive(childId)) throw new Exception("Child survived forced parent termination.");
    Console.WriteLine("PASS: force-killing the host terminates the child automatically.");
}
finally
{
    if (helperStarted && !helper.HasExited) helper.Kill(entireProcessTree: true);
    if (childId > 0 && IsAlive(childId))
    {
        try { Process.GetProcessById(childId).Kill(entireProcessTree: true); }
        catch (ArgumentException) { }
    }
    if (File.Exists(pidPath)) File.Delete(pidPath);
}

Console.WriteLine("All Windows process-lifetime checks passed.");

static Process LaunchLongRunningProcess()
{
    var process = new Process
    {
        StartInfo = new ProcessStartInfo("ping.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };
    process.StartInfo.ArgumentList.Add("-n");
    process.StartInfo.ArgumentList.Add("120");
    process.StartInfo.ArgumentList.Add("127.0.0.1");
    if (!process.Start()) throw new Exception("Failed to start ping.exe test worker.");
    return process;
}

static bool IsAlive(int pid)
{
    try
    {
        using var p = Process.GetProcessById(pid);
        return !p.HasExited;
    }
    catch (ArgumentException) { return false; }
}
