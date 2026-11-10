using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using Embasic.ENote.Storage;
using Microsoft.Win32.SafeHandles;

internal static partial class Program
{
    [SupportedOSPlatform("windows")]
    private static int WindowsStartupExperiment(string[] args)
    {
        string mode = args[0], control = args[1], management = args[2];
        if (mode == "tur32-win-setup")
        {
            using var issuer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            Guid workspace = Guid.NewGuid();
            var pin = new VaultRecoveryPolicyPin(workspace, 7, new string('A', 64));
            File.WriteAllBytes(Path.Combine(management, "operator.pub"), issuer.ExportSubjectPublicKeyInfo());
            File.WriteAllBytes(Path.Combine(management, "minimum.epf"), VaultRecoveryPinFloor.Issue(pin, issuer));
            File.WriteAllText(Path.Combine(management, "workspace"), workspace.ToString("D"));
            return 0;
        }

        string service = args[3], sid = args[4];
        return RunService(service, stop =>
        {
            RunWithReceipt(Path.Combine(control, mode + ".result"), () =>
            {
                RequireServiceToken(sid);
                if (mode == "tur32-win-target")
                    ObserveStartupWindow(control, sid, stop);
                else
                    ProbeStartupWindow(control, management, stop);
            });
            return Task.CompletedTask;
        });
    }

    [SupportedOSPlatform("windows")]
    private static void ObserveStartupWindow(string control, string sid, CancellationToken stop)
    {
        const uint writeDacAndQuery = 0x40008;
        using var token = OpenToken(StartupCurrentProcess(), writeDacAndQuery);
        IntPtr thread = StartupCurrentThread();
        byte[] synthetic = "TUR32 synthetic marker"u8.ToArray();
        IntPtr memory = Marshal.AllocHGlobal(synthetic.Length);
        try
        {
            Marshal.Copy(synthetic, 0, memory, synthetic.Length);
            ApplyFixtureDacl(StartupCurrentProcess(), sid, exposeAccount: true);
            ApplyFixtureDacl(thread, sid, exposeAccount: true);
            ApplyFixtureDacl(token.DangerousGetHandle(), sid, exposeAccount: true);
            WriteFixtureReceipt(Path.Combine(control, "startup-address"), FormattableString.Invariant(
                $"{Environment.ProcessId}|{StartupCurrentThreadId()}|{memory.ToInt64()}"));
            WaitForSignal(control, "peer-opened", stop);
            VaultWindowsServiceProcessBoundary.RestrictCurrentProcess(sid);
            ApplyFixtureDacl(thread, sid, exposeAccount: false);
            ApplyFixtureDacl(token.DangerousGetHandle(), sid, exposeAccount: false);
            WriteFixtureReceipt(Path.Combine(control, "startup-restricted"), "restricted");
            WaitForSignal(control, "peer-finished", stop);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    [SupportedOSPlatform("windows")]
    private static void ProbeStartupWindow(string control, string management, CancellationToken stop)
    {
        WaitForSignal(control, "startup-address", stop);
        string[] address = File.ReadAllText(Path.Combine(control, "startup-address")).Split('|');
        int pid = int.Parse(address[0], CultureInfo.InvariantCulture);
        uint tid = uint.Parse(address[1], CultureInfo.InvariantCulture);
        IntPtr memory = new(long.Parse(address[2], CultureInfo.InvariantCulture));
        if (pid == Environment.ProcessId) throw new InvalidOperationException("Expected separate service processes.");

        using var process = RequireHandle(OpenProcess(0x1010, false, pid)); // QUERY_LIMITED_INFORMATION | VM_READ
        using var thread = RequireHandle(StartupOpenThread(0x40, false, tid)); // THREAD_QUERY_INFORMATION
        using var token = OpenToken(process.DangerousGetHandle(), 8); // TOKEN_QUERY
        WriteFixtureReceipt(Path.Combine(control, "peer-opened"), "opened");
        WaitForSignal(control, "startup-restricted", stop);
        Denied(OpenProcess(0x10, false, pid));
        Denied(StartupOpenThread(0x40, false, tid));
        if (StartupOpenProcessToken(process.DangerousGetHandle(), 8, out var newToken))
        {
            newToken?.Dispose();
            throw new InvalidOperationException("A new token handle unexpectedly opened.");
        }
        int tokenError = Marshal.GetLastPInvokeError();
        newToken?.Dispose();
        Equal(5, tokenError);

        byte[] expected = "TUR32 synthetic marker"u8.ToArray();
        byte[] actual = new byte[expected.Length];
        if (!StartupReadProcessMemory(process, memory, actual, (nuint)actual.Length, out nuint read))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        Equal((nuint)expected.Length, read);
        Equal(Convert.ToHexString(expected), Convert.ToHexString(actual));
        Equal(tid, StartupGetThreadId(thread));
        if (!StartupGetTokenInformation(token, 8, out int tokenType, 4, out uint length))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        Equal(4U, length);
        Equal(1, tokenType);

        string floorFile = Path.Combine(management, "minimum.epf");
        Guid workspace = Guid.Parse(File.ReadAllText(Path.Combine(management, "workspace")));
        Equal(new VaultRecoveryPolicyPin(workspace, 7, new string('A', 64)), VaultRecoveryPinFloor.Read(floorFile,
            workspace, File.ReadAllBytes(Path.Combine(management, "operator.pub"))));
        Throws<UnauthorizedAccessException>(() => File.Open(floorFile, FileMode.Open, FileAccess.Write).Dispose());
        Throws<UnauthorizedAccessException>(() => File.Delete(floorFile));
        WriteFixtureReceipt(Path.Combine(control, "peer-finished"), "finished");
    }

    private static void WaitForSignal(string directory, string name, CancellationToken stop)
    {
        while (!File.Exists(Path.Combine(directory, name)))
        {
            stop.ThrowIfCancellationRequested();
            Thread.Sleep(50);
        }
    }

    private static SafeFileHandle RequireHandle(IntPtr handle)
    {
        if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastPInvokeError());
        return new SafeFileHandle(handle, ownsHandle: true);
    }

    private static void Denied(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            using var unexpected = new SafeFileHandle(handle, ownsHandle: true);
            throw new InvalidOperationException("A new handle unexpectedly opened.");
        }
        Equal(5, Marshal.GetLastPInvokeError());
    }

    private static SafeFileHandle OpenToken(IntPtr process, uint access)
    {
        if (!StartupOpenProcessToken(process, access, out var token))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        return token;
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyFixtureDacl(IntPtr handle, string sid, bool exposeAccount)
    {
        string account = exposeAccount ? "(A;;GA;;;LS)" : "(A;;RC;;;S-1-3-4)";
        var descriptor = new RawSecurityDescriptor($"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;{sid})" + account);
        byte[] bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        if (!StartupSetKernelObjectSecurity(handle, 4, bytes))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    [DllImport("kernel32.dll", EntryPoint = "GetCurrentProcess", ExactSpelling = true)]
    private static extern IntPtr StartupCurrentProcess();

    [DllImport("kernel32.dll", EntryPoint = "GetCurrentThread", ExactSpelling = true)]
    private static extern IntPtr StartupCurrentThread();

    [DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId", ExactSpelling = true)]
    private static extern uint StartupCurrentThreadId();

    [DllImport("kernel32.dll", EntryPoint = "OpenThread", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr StartupOpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint tid);

    [DllImport("kernel32.dll", EntryPoint = "GetThreadId", ExactSpelling = true, SetLastError = true)]
    private static extern uint StartupGetThreadId(SafeFileHandle thread);

    [DllImport("kernel32.dll", EntryPoint = "ReadProcessMemory", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartupReadProcessMemory(SafeFileHandle process, IntPtr address, [Out] byte[] bytes,
        nuint length, out nuint read);

    [DllImport("advapi32.dll", EntryPoint = "OpenProcessToken", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartupOpenProcessToken(IntPtr process, uint access, out SafeFileHandle token);

    [DllImport("advapi32.dll", EntryPoint = "GetTokenInformation", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartupGetTokenInformation(SafeFileHandle token, int kind, out int value, uint capacity, out uint length);

    [DllImport("advapi32.dll", EntryPoint = "SetKernelObjectSecurity", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartupSetKernelObjectSecurity(IntPtr handle, uint kind, byte[] descriptor);
}
