using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DeepseekHarnessDesktop.Infrastructure.Services.Backend;

/// <summary>
///     Windows Job Object 守卫：父进程（本应用）崩溃或被强杀时，
///     KILL_ON_JOB_CLOSE 保证 launcher 与 Host 子进程一并终止，不遗留孤儿进程。
/// </summary>
internal sealed class WindowsProcessTreeGuard : IDisposable
{
    private const int    JobObjectExtendedLimitInformation = 9;
    private const uint   JobObjectLimitKillOnJobClose      = 0x2000;
    private       IntPtr _job;

    private WindowsProcessTreeGuard(IntPtr job)
    {
        _job = job;
    }

    public void Dispose()
    {
        var job = Interlocked.Exchange(ref _job, IntPtr.Zero);
        if (job != IntPtr.Zero) CloseHandle(job);
    }

    /// <summary>在非 Windows 平台返回 null；创建失败时也返回 null（失败不阻塞启动）。</summary>
    public static WindowsProcessTreeGuard? TryCreate()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return null;

        var info = new JobobjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };

        var size = Marshal.SizeOf<JobobjectExtendedLimitInformation>();
        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, (uint)size))
        {
            CloseHandle(job);
            return null;
        }

        return new WindowsProcessTreeGuard(job);
    }

    /// <summary>把进程放入 Job；此后 Job 关闭即终止整个进程树。</summary>
    public void Assign(Process process)
    {
        var job = _job;
        if (job != IntPtr.Zero && !AssignProcessToJobObject(job, process.Handle))
        {
            // 放入失败不致命：正常停止路径仍由 launcher 升级终止兜底。
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob, int jobObjectInformationClass, ref JobobjectExtendedLimitInformation lpJobObjectInformation,
        uint   cbJobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long    PerProcessUserTimeLimit;
        public long    PerJobUserTimeLimit;
        public uint    LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint    ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint    PriorityClass;
        public uint    SchedulingClass;
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
    private struct JobobjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters                     IoInfo;
        public UIntPtr                        ProcessMemoryLimit;
        public UIntPtr                        JobMemoryLimit;
        public UIntPtr                        PeakProcessMemoryUsed;
        public UIntPtr                        PeakJobMemoryUsed;
    }
}
