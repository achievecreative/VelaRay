using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using VelaRay.Core.Runtime;

namespace VelaRay.Maui.Services;

/// <summary>
/// Owns every v2ray-core process through a non-inheritable Windows Job Object.
/// The kernel closes the sole job handle when VelaRay crashes or is force-killed;
/// JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE then terminates all member processes.
/// </summary>
public sealed class WindowsJobObjectLifetime : IChildProcessLifetime
{
    private const uint KillOnJobClose = 0x00002000;
    private const int JobObjectExtendedLimitInformationClass = 9;
    private readonly object _gate = new();
    private readonly SafeJobHandle _job;
    private bool _disposed;

    public WindowsJobObjectLifetime()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Job Objects require Windows.");

        // Null security attributes => non-inheritable handle. Child processes must
        // never retain a copy of the only job handle, or crash cleanup would fail.
        _job = CreateJobObject(nint.Zero, null);
        if (_job.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            _job.Dispose();
            throw new Win32Exception(error, "Unable to create v2ray process lifetime Job Object.");
        }

        var limits = new JobObjectExtendedLimitInformation();
        limits.BasicLimitInformation.LimitFlags = KillOnJobClose;
        if (!SetInformationJobObject(_job, JobObjectExtendedLimitInformationClass, ref limits,
                (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
        {
            var error = Marshal.GetLastWin32Error();
            _job.Dispose();
            throw new Win32Exception(error, "Unable to configure kill-on-close for v2ray processes.");
        }
    }

    public void Attach(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Process.Handle is only valid after Process.Start and carries the
            // PROCESS_SET_QUOTA / PROCESS_TERMINATE rights needed for assignment.
            if (!AssignProcessToJobObject(_job, process.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Unable to protect v2ray.exe with the Windows Job Object. The process will be stopped.");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            // Terminate every remaining associated process, including grandchildren.
            _job.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle() : base(ownsHandle: true) { }
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeJobHandle CreateJobObject(nint jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeJobHandle job, int infoClass,
        ref JobObjectExtendedLimitInformation info, uint infoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeJobHandle job, nint processHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
