using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LanTouchPad;

internal sealed class ConsoleSessionProcess : IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved, Desktop, Title;
        public int X, Y, Width, Height, XCount, YCount, Fill, Flags;
        public short Show, ReservedSize;
        public IntPtr ReservedData, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public uint Id, ThreadId; }
    [StructLayout(LayoutKind.Sequential)] private struct Privilege { public uint Count, Low; public int High; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct JobBasicLimit
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcesses;
        public UIntPtr Affinity;
        public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct JobLimit
    {
        public JobBasicLimit Basic;
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll")] internal static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(SafeProcessHandle process, uint code);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(SafeFileHandle job, int type, ref JobLimit limits, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool LookupPrivilegeValue(string? system, string name, out long value);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, bool disable, ref Privilege state, int length, IntPtr previous, IntPtr needed);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DuplicateTokenEx(SafeAccessTokenHandle token, uint access, IntPtr attributes, int level, int type, out SafeAccessTokenHandle duplicate);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool SetTokenInformation(SafeAccessTokenHandle token, int type, ref uint value, uint length);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessAsUser(SafeAccessTokenHandle token, string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo process);
    private readonly SafeProcessHandle process;
    private readonly SafeFileHandle job;
    public uint Session { get; }
    public bool Exited => WaitForSingleObject(process, 0) == 0;

    public ConsoleSessionProcess(uint session)
    {
        Session = session;
        job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid) throw new Win32Exception();
        try
        {
            var limit = new JobLimit { Basic = new JobBasicLimit { Flags = 0x2000 } };
            if (!SetInformationJobObject(job, 9, ref limit, (uint)Marshal.SizeOf<JobLimit>())) throw new Win32Exception();
            if (!OpenProcessToken(GetCurrentProcess(), 0x2B, out var token)) throw new Win32Exception();
            using (token)
            {
                foreach (var name in new[] { "SeTcbPrivilege", "SeAssignPrimaryTokenPrivilege", "SeIncreaseQuotaPrivilege" })
                {
                    if (!LookupPrivilegeValue(null, name, out long luid)) throw new Win32Exception();
                    var state = new Privilege { Count = 1, Low = (uint)luid, High = (int)(luid >> 32), Attributes = 2 };
                    if (!AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero) || Marshal.GetLastWin32Error() != 0)
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot enable service privilege: " + name);
                }
                if (!DuplicateTokenEx(token, 0x02000000, IntPtr.Zero, 2, 1, out var primary)) throw new Win32Exception();
                using (primary)
                {
                    if (!SetTokenInformation(primary, 12, ref session, sizeof(uint))) throw new Win32Exception();
                    var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Desktop = @"winsta0\default" };
                    string executable = Environment.ProcessPath!;
                    var command = new StringBuilder($"\"{executable}\" --session-receiver");
                    if (!CreateProcessAsUser(primary, executable, command, IntPtr.Zero, IntPtr.Zero, false, 0x08000004, IntPtr.Zero, AppContext.BaseDirectory, ref startup, out var info))
                        throw new Win32Exception();
                    process = new SafeProcessHandle(info.Process, true);
                    try
                    {
                        if (!AssignProcessToJobObject(job, process) || ResumeThread(info.Thread) == uint.MaxValue)
                            throw new Win32Exception();
                    }
                    catch { TerminateProcess(process, 1); process.Dispose(); throw; }
                    finally { CloseHandle(info.Thread); }
                }
            }
        }
        catch { job.Dispose(); throw; }
    }

    public void Stop(EventWaitHandle signal)
    {
        signal.Set();
        if (WaitForSingleObject(process, 5000) != 0) TerminateProcess(process, 1);
    }
    public void Dispose() { job.Dispose(); process.Dispose(); }
}
