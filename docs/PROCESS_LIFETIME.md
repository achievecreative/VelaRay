# VelaRay / V2Fly process lifetime (Windows)

## Required behavior

- **Connect / validation**: both `v2ray.exe run` and `v2ray.exe test` processes must be attached to a Windows Job Object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. If attachment fails, kill the process immediately and fail the operation.
- **Disconnect**: call `V2RayCoreRunner.StopAsync()` to stop the currently running V2Fly process; do not terminate unrelated `v2ray.exe` processes started by other applications.
- **Exit (tray menu / actual window destruction)**: `App.CleanupOnExit()` stops the core, closes the Job Object, stops the local PAC server, restores VelaRay-owned Windows proxy changes, and releases tray resources. All cleanup steps are isolated so a failure in one does not skip others.
- **Close to tray**: hiding/minimizing the window is *not* application exit, so V2Fly deliberately continues running until disconnected or the user chooses Exit.
- **Force-kill / unhandled fatal exception**: no managed `finally`, `Destroying`, or `ProcessExit` handler is guaranteed to run. Windows closes the application's non-inheritable Job Object handle, causing the OS to kill all associated processes (and job descendants). There remains a very short `Process.Start` -> `AssignProcessToJobObject` window; fail-closed behavior covers assignment errors, but avoiding this race entirely requires a suspended/atomic Windows process creation path.
- **Reboot / shutdown**: the OS terminates application and child processes. When a crash prevents proxy restoration, `WindowsSystemProxyService.RecoverAbandonedBackup()` repairs VelaRay-owned proxy changes on the next launch if they remain active. Real-time proxy restoration on hard termination is **not guaranteed** by a Job Object; it would require a separate watchdog process/service.

## Implementation

- `src/VelaRay.Core/Runtime/IChildProcessLifetime.cs`: extensible platform contract.
- `src/VelaRay.Maui/Services/WindowsJobObjectLifetime.cs`: Windows P/Invoke `CreateJobObjectW`, `SetInformationJobObject`, `AssignProcessToJobObject`; safe handle is non-inheritable.
- `src/VelaRay.Core/Runtime/V2RayCoreRunner.cs`: attaches after every process start, handles failures by killing the process tree.
- `src/VelaRay.Maui/App.xaml.cs`: idempotent normal exit cleanup, best effort for managed termination.

## Run on Windows

```powershell
# Verify core behavior and forced-parent-termination behavior.
dotnet run --project tests/VelaRay.WindowsLifetimeTests/VelaRay.WindowsLifetimeTests.csproj -c Release

# Full project audit, MAUI builds and process lifetime checks.
.\scripts\verify-windows.ps1 -SkipPublish
```

The Windows lifetime tests run two checks: closing the Job Object kills a live test child; then killing a stand-in UI host with `Process.Kill(entireProcessTree: false)` also kills the child through the OS Job Object. It does not use V2Fly credentials or open external network connections (ping loopback only).

### Manual smoke scenarios

1. Connect, note `Started protected v2ray-core PID ...` in logs, then choose **Exit** in tray. Verify only the VelaRay-owned PID disappears; confirm previous Windows proxy settings are restored.
2. Connect, then open Task Manager -> End task on **VelaRay.exe**. Verify the recorded `v2ray.exe` PID disappears. Reopen VelaRay to recover proxy backup if Windows settings still point to the now-stopped local proxy.
3. Connect, close the window with "Minimize to tray" on. VelaRay and `v2ray.exe` must keep running. Choose Exit to stop them.
4. Connect, disconnect/reconnect several times, then Exit; no earlier `v2ray.exe` PID may survive.

## Limitations

- This guards only processes launched and assigned by VelaRay, not independent V2Fly instances started externally.
- Job Object process termination is supported by Windows 10/11. Other platforms need an equivalent `IChildProcessLifetime` implementation.
- Windows MAUI compilation and OS-specific tests must be run on Windows with .NET 10 and MAUI workloads; static inspection on Linux cannot establish success.
