using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

internal static partial class Program
{
    [SupportedOSPlatform("windows")]
    private static int Tur33WindowsServices(string[] args)
    {
        string mode = args[0], control = args[1], serviceSid = args[4], peerSid = args[5];
        if (mode == "tur33-win-stop")
            return RunService(args[3], async stop =>
            {
                RequireServiceToken(serviceSid);
                WriteFixtureReceipt(Path.Combine(control, "native-ready"), "ready");
                try { await Task.Delay(Timeout.InfiniteTimeSpan, stop); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                WriteFixtureReceipt(Path.Combine(control, "native-received"), "stop");
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                WaitForSignal(control, "native-release", deadline.Token);
                WriteFixtureReceipt(Path.Combine(control, "tur33-win-stop.result"), "PASS SCM STOP");
            }, reportStopPending: true);

        if (mode == "tur33-win-child")
        {
            RequireServiceToken(serviceSid);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            PublishDefaultIdentity(control, "child");
            WaitForSignal(control, "default-checked", deadline.Token);
            GrantQueryOnly(peerSid);
            WriteFixtureReceipt(Path.Combine(control, "child-query-ready"), "ready");
            WaitForSignal(control, "stop-child", deadline.Token);
            return 0;
        }

        return RunService(args[3], stop =>
        {
            if (mode == "tur33-win-peer")
            {
                RunWithReceipt(Path.Combine(control, "tur33-win-peer.result"), () =>
                {
                    using var token = WindowsIdentity.GetCurrent();
                    Equal("S-1-5-20", token.User!.Value);
                    Equal(true, new WindowsPrincipal(token).IsInRole(new SecurityIdentifier(serviceSid)));
                    var target = ReadDefaultIdentity(control, "target", stop);
                    var child = ReadDefaultIdentity(control, "child", stop);
                    if (target.Process == child.Process || target.Process == Environment.ProcessId ||
                        child.Process == Environment.ProcessId)
                        throw new InvalidOperationException("Expected three distinct process identities.");
                    foreach (var item in new[] { target, child })
                    {
                        Denied(OpenProcess(0x10, false, item.Process));
                        Denied(OpenProcess(0x40, false, item.Process));
                        Denied(StartupOpenThread(0x40, false, item.Thread));
                    }
                    WriteFixtureReceipt(Path.Combine(control, "default-checked"), "ready");
                    WaitForSignal(control, "target-query-ready", stop);
                    WaitForSignal(control, "child-query-ready", stop);
                    foreach (var item in new[] { target, child })
                    {
                        using SafeFileHandle query = RequireHandle(OpenProcess(0x1000, false, item.Process));
                        if (StartupOpenProcessToken(query.DangerousGetHandle(), 8, out var unexpected))
                        {
                            unexpected?.Dispose();
                            throw new InvalidOperationException("The distinct peer opened a service token.");
                        }
                        int error = Marshal.GetLastPInvokeError();
                        unexpected?.Dispose();
                        Equal(5, error);
                    }
                });
                return Task.CompletedTask;
            }

            RequireServiceToken(serviceSid);
            PublishDefaultIdentity(control, "target");
            using var childProcess = StartHarness("tur33-win-child", control, args[2], args[3], serviceSid, peerSid);
            try
            {
                WaitForSignal(control, "default-checked", stop);
                GrantQueryOnly(peerSid);
                WriteFixtureReceipt(Path.Combine(control, "target-query-ready"), "ready");
                WaitForSignal(control, "peer-finished", stop);
            }
            finally
            {
                WriteFixtureReceipt(Path.Combine(control, "stop-child"), "stop");
                if (!childProcess.WaitForExit(8_000))
                {
                    childProcess.Kill();
                    throw new TimeoutException("The service child did not exit.");
                }
                Equal(0, childProcess.ExitCode);
            }
            WriteFixtureReceipt(Path.Combine(control, "tur33-win-target.result"), "PASS child shutdown");
            return Task.CompletedTask;
        });
    }

    private static void PublishDefaultIdentity(string directory, string name) =>
        WriteFixtureReceipt(Path.Combine(directory, name + "-default"), FormattableString.Invariant(
            $"{Environment.ProcessId}|{StartupCurrentThreadId()}"));

    private static (int Process, uint Thread) ReadDefaultIdentity(string directory, string name, CancellationToken stop)
    {
        WaitForSignal(directory, name + "-default", stop);
        string[] values = File.ReadAllText(Path.Combine(directory, name + "-default")).Split('|');
        return (int.Parse(values[0], CultureInfo.InvariantCulture), uint.Parse(values[1], CultureInfo.InvariantCulture));
    }

    [SupportedOSPlatform("windows")]
    private static void GrantQueryOnly(string peerSid)
    {
        string sid = new SecurityIdentifier(peerSid).Value;
        var descriptor = new RawSecurityDescriptor($"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;LS)(A;;0x1000;;;{sid})");
        byte[] bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        if (!StartupSetKernelObjectSecurity(StartupCurrentProcess(), 4, bytes))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
    }
}
