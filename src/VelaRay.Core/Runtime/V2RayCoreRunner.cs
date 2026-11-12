using System.Diagnostics;
using VelaRay.Core.Configuration;

namespace VelaRay.Core.Runtime;

public sealed class V2RayCoreRunner : IDisposable
{
    private readonly IChildProcessLifetime _processLifetime;
    private Process? _process;

    public V2RayCoreRunner(IChildProcessLifetime processLifetime)
    {
        _processLifetime = processLifetime ?? throw new ArgumentNullException(nameof(processLifetime));
    }

    public event Action<string>? LogReceived;
    public event Action? UnexpectedExit;
    private bool _stopping;
    public bool IsRunning => _process is { HasExited: false };
    /// <summary>Absolute path passed to the running v2ray-core process, or null when stopped.</summary>
    public string? RunningConfigPath { get; private set; }
    public string? RunningConfigFormat { get; private set; }

    /// <summary>
    /// V2Fly's V5 config loader is named "jsonv5", not "json5". Always pass
    /// the format explicitly: a filename ending in .v5.json is otherwise
    /// auto-detected as a regular .json file by some v2ray-core versions.
    /// Kept separate from ProcessStartInfo so both test/run paths are
    /// regression-tested with exactly the same argument format.
    /// </summary>
    public const string ConfigFormat = "jsonv5";

    public static string[] BuildConfigArguments(string command, string configPath) =>
        BuildConfigArguments(command, configPath, ConfigFlavor.V5);

    public static string[] BuildConfigArguments(string command, string configPath, string format)
    {
        if (command is not ("run" or "test"))
            throw new ArgumentOutOfRangeException(nameof(command), "Only v2ray run/test are supported.");
        if (string.IsNullOrWhiteSpace(configPath))
            throw new ArgumentException("A config file path is required.", nameof(configPath));
        return [command, "-format", ConfigFlavor.Loader(format), "-c", Path.GetFullPath(configPath)];
    }

    private static ProcessStartInfo StartInfo(string executable, string command, string configPath, string format)
    {
        var fullExecutable = Path.GetFullPath(executable);
        var info = new ProcessStartInfo(fullExecutable)
        {
            WorkingDirectory = Path.GetDirectoryName(fullExecutable)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in BuildConfigArguments(command, configPath, format))
            info.ArgumentList.Add(argument);
        // V2Fly searches this directory for geosite.dat, geoip.dat, and custom .dat assets.
        // Apply identically to both 'test' and 'run'. This avoids depending on CWD.
        info.Environment["V2RAY_LOCATION_ASSET"] = Path.GetDirectoryName(fullExecutable)!;
        return info;
    }

    // This is for the UI diagnostic log only. ProcessStartInfo.ArgumentList,
    // not a single shell-escaped string, controls actual process invocation.
    private static string DisplayCommand(ProcessStartInfo info) =>
        $"\"{info.FileName}\" " + string.Join(" ", info.ArgumentList.Select(argument =>
            argument.Any(char.IsWhiteSpace) ? $"\"{argument}\"" : argument));

    public Task TestConfigAsync(string executable, string configPath, CancellationToken ct = default) =>
        TestConfigAsync(executable, configPath, ConfigFlavor.V5, ct);

    public async Task TestConfigAsync(string executable, string configPath, string format, CancellationToken ct = default)
    {
        if (!File.Exists(executable))
            throw new FileNotFoundException("v2ray-core is missing. Place v2ray.exe, geoip.dat and geosite.dat in the v2ray-core folder.", executable);
        using var check = new Process { StartInfo = StartInfo(executable, "test", configPath, format) };
        var displayedCommand = DisplayCommand(check.StartInfo);
        LogReceived?.Invoke($"V2Fly {ConfigFlavor.Normalize(format)} validation command: " + displayedCommand);
        if (!check.Start()) throw new InvalidOperationException("Cannot start V2Ray config validator.");
        try { _processLifetime.Attach(check); }
        catch
        {
            // Fail closed: never leave an unowned validator process running.
            try { if (!check.HasExited) check.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw;
        }
        var standardOutput = check.StandardOutput.ReadToEndAsync(ct);
        var standardError = check.StandardError.ReadToEndAsync(ct);
        try { await check.WaitForExitAsync(ct); }
        catch (OperationCanceledException)
        {
            if (!check.HasExited) check.Kill(entireProcessTree: true);
            throw;
        }
        var output = await standardOutput;
        var errors = await standardError;
        if (check.ExitCode != 0)
            throw new InvalidOperationException($"V2Fly {ConfigFlavor.Normalize(format)} validation failed (exit {check.ExitCode}).\nCommand: {displayedCommand}\n{output}\n{errors}");
        LogReceived?.Invoke($"V2Fly {ConfigFlavor.Normalize(format)} ({ConfigFlavor.Loader(format)}) validation successful.");
    }

    public Task StartAsync(string executable, string configPath, CancellationToken ct = default) =>
        StartAsync(executable, configPath, ConfigFlavor.V5, ct);

    public async Task StartAsync(string executable, string configPath, string format, CancellationToken ct = default)
    {
        if (IsRunning) return;
        await TestConfigAsync(executable, configPath, format, ct);
        _stopping = false;
        var process = new Process { StartInfo = StartInfo(executable, "run", configPath, format), EnableRaisingEvents = true };
        LogReceived?.Invoke($"V2Fly {ConfigFlavor.Normalize(format)} startup command: " + DisplayCommand(process.StartInfo));
        process.OutputDataReceived += (_, e) => { if (e.Data is { } line) LogReceived?.Invoke(line); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is { } line) LogReceived?.Invoke(line); };
        process.Exited += (_, _) =>
        {
            // Old Exited callbacks may arrive after a restart: never clear the
            // config or roll back Windows settings of a newer v2ray process.
            if (!ReferenceEquals(_process, process)) return;
            int code;
            try { code = process.ExitCode; } catch { code = -1; }
            LogReceived?.Invoke($"V2Ray exited with code {code}.");
            RunningConfigPath = null;
            RunningConfigFormat = null;
            if (!_stopping) UnexpectedExit?.Invoke();
        };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Failed to launch v2ray.exe.");
            // Assign immediately: if the GUI is killed, Windows closes our job
            // handle and kills v2ray even though no C# shutdown event can run.
            _processLifetime.Attach(process);
            _process = process;
            RunningConfigPath = Path.GetFullPath(configPath);
            RunningConfigFormat = ConfigFlavor.Normalize(format);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            LogReceived?.Invoke($"Started protected v2ray-core PID {process.Id}.");
            await Task.Delay(250, ct);
            if (process.HasExited)
                throw new InvalidOperationException($"v2ray-core exited immediately, code {process.ExitCode}. Check log output.");
        }
        catch
        {
            // Includes failed job assignment, failed stream setup, cancellations,
            // and a core that exited immediately. Never leave an unmanaged child.
            _stopping = true;
            if (ReferenceEquals(_process, process)) _process = null;
            RunningConfigPath = null;
            RunningConfigFormat = null;
            try
            {
                if (process.Id != 0 && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
            catch (InvalidOperationException) { }
            process.Dispose();
            throw;
        }
    }

    public async Task StopAsync()
    {
        _stopping = true;
        var process = _process;
        _process = null;
        RunningConfigPath = null;
        RunningConfigFormat = null;
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException) { }
        finally { process.Dispose(); }
    }

    public void Dispose()
    {
        _stopping = true;
        RunningConfigPath = null;
        RunningConfigFormat = null;
        if (_process is not null)
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            _process.Dispose();
            _process = null;
        }
        GC.SuppressFinalize(this);
    }
}
