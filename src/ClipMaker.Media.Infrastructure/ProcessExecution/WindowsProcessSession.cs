using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ClipMaker.Media.Infrastructure.ProcessExecution;

[SupportedOSPlatform("windows10.0")]
internal sealed class WindowsProcessSession : IProcessSession
{
    private readonly SafeJobHandle job;
    private readonly SafeProcessHandle process;
    private readonly WaitHandle waitHandle;
    private readonly RegisteredWaitHandle registration;
    private readonly TaskCompletionSource rootExit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Stream StandardOutput { get; }
    public Stream StandardError { get; }
    public Task RootExit => rootExit.Task;
    public bool RootHasExited => WaitForSingleObject(process, 0) switch
    {
        0 => true,
        258 => false,
        _ => throw new Win32Exception(Marshal.GetLastWin32Error())
    };
    public int? ExitCode
    {
        get
        {
            if (!RootHasExited) return null;
            if (!GetExitCodeProcess(process, out uint code)) throw new Win32Exception();
            return unchecked((int)code);
        }
    }
    public bool AllProcessesExited
    {
        get
        {
            if (!QueryInformationJobObject(job, 1, out JobAccounting information,
                (uint)Marshal.SizeOf<JobAccounting>(), out _)) throw new Win32Exception();
            return information.ActiveProcesses == 0;
        }
    }

    private WindowsProcessSession(SafeJobHandle job, SafeProcessHandle process,
        Stream stdout, Stream stderr)
    {
        this.job = job;
        this.process = process;
        StandardOutput = stdout;
        StandardError = stderr;
        waitHandle = new ProcessWaitHandle(process);
        try
        {
            registration = ThreadPool.RegisterWaitForSingleObject(waitHandle,
                (_, _) => rootExit.TrySetResult(), null, Timeout.Infinite, executeOnlyOnce: true);
        }
        catch { waitHandle.Dispose(); throw; }
    }

    public static WindowsProcessSession Start(ProcessRequest request, Action<uint>? afterCreation = null)
    {
        SafeJobHandle job = CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid) { job.Dispose(); throw new Win32Exception(); }
        NamedPipeServerStream? stdout = null;
        NamedPipeServerStream? stderr = null;
        SafeProcessHandle? process = null;
        try
        {
            var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = 0x2000 } };
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
                throw new Win32Exception();
            var outputPipe = CreateOutputPipe();
            stdout = outputPipe.Server;
            using var outputWriter = outputPipe.Client;
            var errorPipe = CreateOutputPipe();
            stderr = errorPipe.Server;
            using var errorWriter = errorPipe.Client;
            using var input = CreateFileW("NUL", 0x80000000, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
            if (input.IsInvalid) throw new Win32Exception();
            if (!SetHandleInformation(input, 1, 1)) throw new Win32Exception();

            // JOB_LIST assigns containment as part of creation, before any tool code can fork.
            // HANDLE_LIST prevents concurrently launched tools inheriting each other's pipe writers.
            using var attributes = new AttributeList(
                [input.DangerousGetHandle(), outputWriter.SafePipeHandle.DangerousGetHandle(), errorWriter.SafePipeHandle.DangerousGetHandle()],
                job.DangerousGetHandle());
            var startup = new StartupInfoEx
            {
                Startup = new StartupInfo
                {
                    Size = (uint)Marshal.SizeOf<StartupInfoEx>(), Flags = 0x100,
                    Input = input.DangerousGetHandle(),
                    Output = outputWriter.SafePipeHandle.DangerousGetHandle(),
                    Error = errorWriter.SafePipeHandle.DangerousGetHandle()
                },
                Attributes = attributes.Pointer
            };
            var commandLine = new StringBuilder(WindowsCommandLine.Encode(
                new[] { request.ExecutablePath }.Concat(request.Arguments)));
            if (!CreateProcessW(request.ExecutablePath, commandLine, IntPtr.Zero, IntPtr.Zero,
                true, 0x00080000 | 0x08000000, IntPtr.Zero, request.WorkingDirectory, ref startup, out var created))
                throw new Win32Exception();
            process = new SafeProcessHandle(created.Process, ownsHandle: true);
            using var thread = new SafeWaitHandle(created.Thread, ownsHandle: true);
            afterCreation?.Invoke(created.ProcessId);
            return new(job, process, stdout, stderr);
        }
        catch (Exception startFailure)
        {
            ProcessStartCleanupException? unconfirmed = null;
            if (process is not null)
            {
                bool rootExited = false;
                bool descendantsExited = false;
                Exception? cleanupFailure = null;
                try
                {
                    if (!TerminateJobObject(job, 1)) throw new Win32Exception();
                    var elapsed = Stopwatch.StartNew();
                    while (true)
                    {
                        rootExited = WaitForSingleObject(process, 0) == 0;
                        if (!QueryInformationJobObject(job, 1, out JobAccounting information,
                            (uint)Marshal.SizeOf<JobAccounting>(), out _)) throw new Win32Exception();
                        descendantsExited = information.ActiveProcesses == 0;
                        if (rootExited && descendantsExited || elapsed.Elapsed >= TimeSpan.FromSeconds(10)) break;
                        Thread.Sleep(20);
                    }
                }
                catch (Win32Exception exception) { cleanupFailure = exception; }
                if (!rootExited || !descendantsExited || cleanupFailure is not null)
                    unconfirmed = new(startFailure, rootExited, descendantsExited, cleanupFailure);
            }
            // Closing the job is a final fallback, never proof of completed termination.
            job.Dispose();
            process?.Dispose();
            stdout?.Dispose();
            stderr?.Dispose();
            if (unconfirmed is not null) throw unconfirmed;
            throw;
        }
    }

    private static (NamedPipeServerStream Server, NamedPipeClientStream Client) CreateOutputPipe()
    {
        string name = "ClipMaker-Process-" + Guid.NewGuid();
        var server = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        NamedPipeClientStream? client = null;
        try
        {
            Task connected = server.WaitForConnectionAsync();
            client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.None,
                TokenImpersonationLevel.Anonymous, HandleInheritability.Inheritable);
            client.Connect(5000);
            connected.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            return (server, client);
        }
        catch { client?.Dispose(); server.Dispose(); throw; }
    }

    public void Terminate()
    {
        if (!TerminateJobObject(job, 1)) throw new Win32Exception();
    }

    public void Dispose()
    {
        // Only this invocation's job is closed. No name/PID-based broad process termination.
        job.Dispose();
        registration.Unregister(null);
        rootExit.TrySetCanceled();
        StandardOutput.Dispose();
        StandardError.Dispose();
        waitHandle.Dispose();
        process.Dispose();
    }

    private sealed class AttributeList : IDisposable
    {
        public IntPtr Pointer { get; private set; }
        private IntPtr handles;
        private IntPtr jobs;
        private bool initialized;

        public AttributeList(IntPtr[] inheritedHandles, IntPtr job)
        {
            try
            {
                nuint size = 0;
                InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref size);
                if (size == 0) throw new Win32Exception();
                Pointer = Marshal.AllocHGlobal(checked((nint)size));
                if (!InitializeProcThreadAttributeList(Pointer, 2, 0, ref size)) throw new Win32Exception();
                initialized = true;
                handles = Marshal.AllocHGlobal(IntPtr.Size * inheritedHandles.Length);
                for (int index = 0; index < inheritedHandles.Length; index++)
                    Marshal.WriteIntPtr(handles, index * IntPtr.Size, inheritedHandles[index]);
                jobs = Marshal.AllocHGlobal(IntPtr.Size);
                Marshal.WriteIntPtr(jobs, job);
                if (!UpdateProcThreadAttribute(Pointer, 0, 0x20002, handles,
                    (nuint)(IntPtr.Size * inheritedHandles.Length), IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
                if (!UpdateProcThreadAttribute(Pointer, 0, 0x2000D, jobs,
                    (nuint)IntPtr.Size, IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
            }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            if (initialized) { DeleteProcThreadAttributeList(Pointer); initialized = false; }
            if (Pointer != IntPtr.Zero) { Marshal.FreeHGlobal(Pointer); Pointer = IntPtr.Zero; }
            if (handles != IntPtr.Zero) { Marshal.FreeHGlobal(handles); handles = IntPtr.Zero; }
            if (jobs != IntPtr.Zero) { Marshal.FreeHGlobal(jobs); jobs = IntPtr.Zero; }
        }
    }

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle() : base(ownsHandle: true) { }
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(SafeProcessHandle process) =>
            SafeWaitHandle = new SafeWaitHandle(process.DangerousGetHandle(), ownsHandle: false);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint LimitFlags;
        public nuint MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveLimit;
        public nuint Affinity;
        public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct JobAccounting
    {
        public long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime;
        public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint Size;
        public IntPtr Reserved, Desktop, Title;
        public uint X, Y, Width, Height, XCharacters, YCharacters, Fill, Flags;
        public ushort ShowWindow, ReservedBytes;
        public IntPtr ReservedData, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx { public StartupInfo Startup; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeJobHandle CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeJobHandle job, int kind, ref ExtendedLimits limits, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(SafeJobHandle job, int kind, out JobAccounting data, uint size, out uint returned);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(SafeJobHandle job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr attributes,
        uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(SafeHandle handle, uint mask, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint attribute,
        IntPtr value, nuint size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processAttributes,
        IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags,
        IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInformation information);
}
